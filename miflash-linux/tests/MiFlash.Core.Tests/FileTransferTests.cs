using System.IO;
using System.Text;
using MiFlash.Core.Io;
using MiFlash.Core.Protocol;
using MiFlash.Core.Runtime;
using MiFlash.Core.Tests.Fakes;
using MiFlash.Core.Transfer;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>Tests de FileTransfer sur FakeTransport — program rawprogram, sparse (M1.4/M1.7).</summary>
public class FileTransferTests : IDisposable
{
    private readonly string _tempDir;

    public FileTransferTests()
    {
        Log.Sink = static (_, _) => { };
        _tempDir = Path.Combine(Path.GetTempPath(), "miflash-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    private static (Comm Comm, FakeTransport Transport) CreateComm(int sectorSize = 4096, int bufferSectors = 256)
    {
        var transport = new FakeTransport();
        transport.Open();
        var comm = new Comm(transport)
        {
            intSectorSize = sectorSize,
            m_dwBufferSectors = bufferSectors,
        };
        return (comm, transport);
    }

    private string CreateRawImage(int size)
    {
        string path = Path.Combine(_tempDir, "raw_" + size + ".img");
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 251);
        }
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Construit une image sparse Android minimaliste (header + chunks).</summary>
    private string CreateSparseImage(int blockSize, params (ushort Type, uint ChunkBlocks, byte[] Pattern)[] chunks)
    {
        using var ms = new MemoryStream();
        var header = new Images.SparseImageHeader
        {
            uMagic = Images.Sparse.SPARSE_HEADER_MAGIC,
            uMajorVersion = 1,
            uMinorVersion = 0,
            uFileHeaderSize = 28,
            uChunkHeaderSize = 12,
            uBlockSize = (uint)blockSize,
            uTotalChunks = (uint)chunks.Length,
            uTotalBlocks = 0,
            uImageChecksum = 0,
        };
        uint totalBlocks = 0;
        foreach (var (_, blocks, _) in chunks)
        {
            totalBlocks += blocks;
        }
        header.uTotalBlocks = totalBlocks;
        ms.Write(Protocol.CommandFormat.StructToBytes(header, 28), 0, 28);

        foreach (var (type, chunkBlocks, pattern) in chunks)
        {
            uint chunkBytes = chunkBlocks * (uint)blockSize;
            uint totalSize = type switch
            {
                ushort t when t == Images.Sparse.SPARSE_CHUNK_TYPE_RAW => 12 + chunkBytes,
                ushort t when t == Images.Sparse.SPARSE_CHUNK_TYPE_FILL => 12 + 4,
                _ => 12,
            };
            var chunkHeader = new Images.SparseChunkHeader
            {
                uChunkType = type,
                uReserved1 = 0,
                uChunkSize = chunkBlocks,
                uTotalSize = totalSize,
            };
            ms.Write(Protocol.CommandFormat.StructToBytes(chunkHeader, 12), 0, 12);
            if (type == Images.Sparse.SPARSE_CHUNK_TYPE_RAW)
            {
                byte[] data = new byte[chunkBytes];
                for (int i = 0; i < chunkBytes; i++)
                {
                    data[i] = pattern[i % pattern.Length];
                }
                ms.Write(data, 0, data.Length);
            }
            else if (type == Images.Sparse.SPARSE_CHUNK_TYPE_FILL)
            {
                ms.Write(pattern, 0, 4);
            }
        }

        string path = Path.Combine(_tempDir, "sparse.img");
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    [Fact]
    public void WriteFile_sends_program_xml_then_raw_sectors()
    {
        string image = CreateRawImage(4096);
        (Comm comm, FakeTransport transport) = CreateComm();
        using var transfer = new FileTransfer("FAKE9008", image);

        transfer.WriteFile(comm, "0", "1", image, "0", "0", "4096", "0", "", chkAck: false, chunkCount: null);

        Assert.Equal(2, transport.WrittenChunks.Count);
        string xml = Encoding.UTF8.GetString(transport.WrittenChunks[0]);
        Assert.Contains("<program", xml);
        Assert.Contains("SECTOR_SIZE_IN_BYTES=\"4096\"", xml);
        Assert.Contains("num_partition_sectors=\"1\"", xml);
        Assert.Contains("start_sector=\"0\"", xml);
        byte[] expected = File.ReadAllBytes(image);
        Assert.Equal(expected, transport.WrittenChunks[1]);
    }

    [Fact]
    public void WriteFile_respects_partition_sector_limit()
    {
        string image = CreateRawImage(4096 * 3);
        (Comm comm, FakeTransport transport) = CreateComm();
        using var transfer = new FileTransfer("FAKE9008", image);

        // Partition limitée à 2 secteurs sur 3 disponibles.
        transfer.WriteFile(comm, "0", "2", image, "0", "0", "4096", "0", "", chkAck: false, chunkCount: null);

        string xml = Encoding.UTF8.GetString(transport.WrittenChunks[0]);
        Assert.Contains("num_partition_sectors=\"2\"", xml);
        // 2 secteurs de données écrits (2e chunk).
        long dataBytes = 0;
        for (int i = 1; i < transport.WrittenChunks.Count; i++)
        {
            dataBytes += transport.WrittenChunks[i].Length;
        }
        Assert.Equal(4096 * 2, dataBytes);
    }

    [Fact]
    public void WriteFile_zero_partition_sectors_caps_to_file_size()
    {
        string image = CreateRawImage(4096);
        (Comm comm, FakeTransport transport) = CreateComm();
        using var transfer = new FileTransfer("FAKE9008", image);

        // « 0 » = pas de limite de partition : le nombre de secteurs est plafonné
        // à la taille réelle du fichier (1 secteur ici).
        transfer.WriteFile(comm, "0", "0", image, "0", "0", "4096", "0", "", chkAck: false, chunkCount: null);

        string xml = Encoding.UTF8.GetString(transport.WrittenChunks[0]);
        Assert.Contains("num_partition_sectors=\"1\"", xml);
    }

    [Fact]
    public void WriteSparseFileToDevice_streams_raw_chunk_with_ack()
    {
        string image = CreateSparseImage(4096, (Images.Sparse.SPARSE_CHUNK_TYPE_RAW, 1, new byte[] { 0xAB }));
        (Comm comm, FakeTransport transport) = CreateComm();
        using var transfer = new FileTransfer("FAKE9008", image);
        // 1 chunk RAW => 1 ACK groupé de 2 réponses (data + status).
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>" +
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");

        transfer.WriteSparseFileToDevice(comm, "0", "0", image, "0", "4096", "0", "");

        Assert.Equal(2, transport.WrittenChunks.Count);
        string xml = Encoding.UTF8.GetString(transport.WrittenChunks[0]);
        Assert.Contains("num_partition_sectors=\"1\"", xml);
        byte[] data = transport.WrittenChunks[1];
        Assert.Equal(4096, data.Length);
        Assert.All(data, b => Assert.Equal(0xAB, b));
    }

    [Fact]
    public void WriteSparseFileToDevice_expands_fill_chunk()
    {
        string image = CreateSparseImage(4096, (Images.Sparse.SPARSE_CHUNK_TYPE_FILL, 1, new byte[] { 0x11, 0x22, 0x33, 0x44 }));
        (Comm comm, FakeTransport transport) = CreateComm();
        using var transfer = new FileTransfer("FAKE9008", image);
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>" +
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");

        transfer.WriteSparseFileToDevice(comm, "0", "0", image, "0", "4096", "0", "");

        string xml = Encoding.UTF8.GetString(transport.WrittenChunks[0]);
        Assert.Contains("num_partition_sectors=\"1\"", xml);
        byte[] data = transport.WrittenChunks[1];
        Assert.Equal(4096, data.Length);
        Assert.Equal(0x11, data[0]);
        Assert.Equal(0x44, data[3]);
        Assert.Equal(0x11, data[4]); // motif répété
    }

    [Fact]
    public void WriteSparseFileToDevice_skips_dontcare_chunks()
    {
        string image = CreateSparseImage(4096,
            (Images.Sparse.SPARSE_CHUNK_TYPE_DONTCARE, 2, System.Array.Empty<byte>()),
            (Images.Sparse.SPARSE_CHUNK_TYPE_RAW, 1, new byte[] { 0xCD }));
        (Comm comm, FakeTransport transport) = CreateComm();
        using var transfer = new FileTransfer("FAKE9008", image);
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>" +
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");

        transfer.WriteSparseFileToDevice(comm, "0", "0", image, "0", "4096", "0", "");

        // Le chunk RAW démarre au secteur 2 (2 secteurs DONTCARE sautés).
        string xml = Encoding.UTF8.GetString(transport.WrittenChunks[0]);
        Assert.Contains("start_sector=\"2\"", xml);
    }

    [Fact]
    public void Bad_magic_sparse_image_is_rejected()
    {
        string image = CreateRawImage(4096);
        (Comm comm, FakeTransport transport) = CreateComm();
        using var transfer = new FileTransfer("FAKE9008", image);

        // raw image : pas de magic sparse => ERROR_BAD_FORMAT.
        Assert.Throws<System.Exception>(() =>
            transfer.WriteSparseFileToDevice(comm, "0", "0", image, "0", "4096", "0", ""));
    }

    [Fact]
    public void GetFileSize_reports_file_length()
    {
        string image = CreateRawImage(4096 * 2 + 123);
        using var transfer = new FileTransfer("FAKE9008", image);
        Assert.Equal(4096 * 2 + 123, transfer.getFileSize());
    }

    [Fact]
    public void MemImg_quick_path_reads_from_memory()
    {
        string image = CreateRawImage(4096);
        MemImg.isHighSpeed = true;
        try
        {
            Assert.Equal(4096, MemImg.mapImg(image));
            byte[] bytes = MemImg.GetBytesFromFile(image, 100, 16, out float percent);
            Assert.Equal(16, bytes.Length);
            Assert.True(percent > 0f);
            byte[] expected = File.ReadAllBytes(image);
            Assert.Equal(expected[100], bytes[0]);
        }
        finally
        {
            MemImg.isHighSpeed = false;
            MemImg.Destroy();
        }
    }
}
