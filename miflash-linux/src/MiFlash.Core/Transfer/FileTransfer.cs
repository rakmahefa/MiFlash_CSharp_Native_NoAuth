using System;
using System.IO;
using System.Text;
using MiFlash.Core.Firehose;
using MiFlash.Core.Images;
using MiFlash.Core.Io;
using MiFlash.Core.Protocol;
using MiFlash.Core.Runtime;
using MiFlash.Core.Transport;

namespace MiFlash.Core.Transfer;

/// <summary>
/// Transfert de fichiers vers/depuis la cible via Firehose (M1.4) — port de
/// XiaoMiFlash.code.bl/FileTransfer.cs. Écriture par secteurs (rawprogram),
/// lecture (backup), images sparse, FFU, chunk ACK.
/// Différences volontaires : <see cref="Comm"/> au lieu de SerialPortDevice,
/// quick transfer via <see cref="MemImg"/> (désactivé par défaut, comme
/// le code Windows d'origine), statuts via <see cref="FlashStatus"/>.
/// </summary>
public class FileTransfer : IDisposable
{
    protected FileStream fileStream;

    public string filePath;

    public string portName;

    private long fileLength;

    public FileTransfer(string port, string filePath)
    {
        portName = port;
        this.filePath = filePath;
        FlashStatus.UpdateDeviceStatus(portName, 0f, "flashing " + filePath, "flashing", isDone: false);
        openFile(filePath, isWriteFile: false);
    }

    public FileTransfer(string port, string filePath, bool isWriteFile)
    {
        portName = port;
        this.filePath = filePath;
        FlashStatus.UpdateDeviceStatus(portName, 0f, "flashing " + filePath, "flashing", isDone: false);
        createFile(filePath);
        openFile(filePath, isWriteFile);
    }

    private bool createFile(string filePath)
    {
        try
        {
            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            }
            if (!File.Exists(filePath))
            {
                File.Create(filePath).Close();
            }
            return true;
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message, ex);
        }
    }

    private bool openFile(string filePath, bool isWriteFile)
    {
        this.filePath = filePath;
        if (MemImg.isHighSpeed && !MemImg.IsQuickExcluded(filePath))
        {
            fileLength = MemImg.mapImg(filePath);
            Log.w(portName, "Image " + filePath + " ,quick transfer");
        }
        try
        {
            FileInfo fileInfo = new FileInfo(filePath);
            fileLength = fileInfo.Length;
            if (isWriteFile)
            {
                fileStream = File.OpenWrite(filePath);
            }
            else
            {
                fileStream = File.OpenRead(filePath);
            }
            return true;
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message, ex);
        }
    }

    public int transfer(ITransport port, int offset, int size)
    {
        if (!port.IsOpen)
        {
            _ = port.PortName + " is not open";
            return 0;
        }
        int n = 0;
        byte[] bytesFromFile = GetBytesFromFile(offset, size, out n);
        port.Write(bytesFromFile, 0, size);
        return n;
    }

    public void ReadFile(Comm comm, string strPartitionStartSector, string strPartitionSectorNumber, string pszImageFile, string strFileStartSector, string strFilesize, string strFileSectorOffset, string sector_size, string physical_partition_number, string addtionalFirehose, bool chkAck, int? chunkCount)
    {
        long num = Convert.ToInt64(strPartitionSectorNumber);
        if (num == 0L)
        {
            num = 2147483647L;
        }
        Convert.ToInt64(strFileStartSector);
        long num2 = Convert.ToInt64(strFileSectorOffset);
        long num3 = Convert.ToInt64(sector_size);
        long num4 = Convert.ToInt64(physical_partition_number);
        string command = string.Format(FirehoseCommands.FIREHOSE_READ, num3, num, strPartitionStartSector, num4, strFilesize, addtionalFirehose);
        comm.SendCommand(command);
        int num5 = 0;
        bool flag = true;
        bool flag2 = false;
        bool flag3 = false;
        byte[] array = null;
        byte[] array2 = null;
        byte[] array3 = null;
        byte[] array4 = null;
        long num6 = num2;
        do
        {
            array = null;
            array2 = null;
            array3 = null;
            array4 = null;
            flag3 = false;
            array2 = comm.getRecData();
            num5 = comm.recData.Length;
            Log.w(comm.PortName, $"aaa read file readDataSize{num5},offset{num6}");
            if (flag)
            {
                StringBuilder stringBuilder = new StringBuilder();
                for (int i = 0; i < num5; i++)
                {
                    stringBuilder.Append(Convert.ToChar(array2[i]).ToString());
                }
                string text = stringBuilder.ToString();
                Log.w(comm.PortName, "rsp:" + text);
                if (text.IndexOf("response value") >= 0)
                {
                    if (text.IndexOf("response value=\"ACK\" rawmode=\"true\"") < 0)
                    {
                        throw new Exception("did not detect ACK from target.");
                    }
                    int startIndex = text.IndexOf("response value=\"ACK\" rawmode=\"true\"");
                    int num7 = text.IndexOf("</data>", startIndex);
                    Log.w(comm.PortName, "first read i:" + startIndex + "j:" + num7);
                    flag = false;
                    flag3 = true;
                    array = new byte[num5 - 7 - num7];
                    Array.Copy(array2, num7 + 7, array, 0, num5 - num7 - 7);
                    num5 = num5 - num7 - 7;
                }
            }
            else
            {
                array = array2;
                flag3 = true;
            }
            if (num5 - 210 > 0)
            {
                array3 = new byte[240];
                Array.Copy(array, num5 - 240, array3, 0, 240);
                StringBuilder stringBuilder2 = new StringBuilder();
                for (int j = 0; j < 240; j++)
                {
                    stringBuilder2.Append(Convert.ToChar(array3[j]).ToString());
                }
                string text2 = stringBuilder2.ToString();
                Log.w(comm.PortName, "rsp" + text2);
                if (text2.IndexOf("response value=\"ACK\" rawmode=\"false\"") >= 0)
                {
                    int num8 = text2.IndexOf("<?xml version=\"1.0\"");
                    flag2 = true;
                    num5 -= 240 - num8;
                    Log.w(comm.PortName, "last read i" + num8);
                    array4 = new byte[num5];
                    Array.Copy(array, 0, array4, 0, num5);
                    array = null;
                    array = array4;
                }
            }
            Log.w(comm.PortName, $"bbb read file readDataSize{num5},offset{num6}");
            if (num5 > 0 && flag3)
            {
                SetBytesToFile(array, num6);
                num6 += num5;
            }
        }
        while (!flag2);
    }

    public void WriteFile(Comm comm, string strPartitionStartSector, string strPartitionSectorNumber, string pszImageFile, string strFileStartSector, string strFileSectorOffset, string sector_size, string physical_partition_number, string addtionalFirehose, bool chkAck, int? chunkCount)
    {
        long num = Convert.ToInt64(strPartitionSectorNumber);
        if (num == 0L)
        {
            num = 2147483647L;
        }
        long num2 = Convert.ToInt64(strFileStartSector);
        long num3 = Convert.ToInt64(strFileSectorOffset);
        long num4 = Convert.ToInt64(sector_size);
        long num5 = Convert.ToInt64(physical_partition_number);
        long num6 = (getFileSize() + num4 - 1) / num4;
        if (num6 - num2 > num)
        {
            num6 = num2 + num;
        }
        else
        {
            num = num6 - num2;
        }
        string command = string.Format(FirehoseCommands.FIREHOSE_PROGRAM, num4, num, strPartitionStartSector, num5, addtionalFirehose);
        comm.SendCommand(command);
        for (long num7 = num2; num7 < num6; num7 += comm.m_dwBufferSectors)
        {
            long num8 = num6 - num7;
            num8 = ((num8 < comm.m_dwBufferSectors) ? num8 : comm.m_dwBufferSectors);
            long offset = num3 + num4 * num7;
            int size = (int)(num4 * num8);
            int n = 0;
            byte[] bytesFromFile = GetBytesFromFile(offset, size, out n);
            comm.WritePort(bytesFromFile, 0, bytesFromFile.Length);
        }
        if (chkAck)
        {
            string msg = null;
            if (!comm.chkRspAck(out msg, chunkCount.Value))
            {
                throw new Exception(msg);
            }
        }
    }

    public void WriteFfuFile(Comm comm, string strPartitionStartSector, string strPartitionSectorNumber, string pszImageFile, string strFileStartSector, string strFileSectorOffset, string sector_size, string physical_partition_number, string addtionalFirehose, bool chkAck, int? chunkCount)
    {
        long num = Convert.ToInt64(strPartitionSectorNumber);
        if (num == 0L)
        {
            num = 2147483647L;
        }
        long num2 = Convert.ToInt64(strFileStartSector);
        long num3 = Convert.ToInt64(strFileSectorOffset);
        long num4 = Convert.ToInt64(sector_size);
        long num5 = Convert.ToInt64(physical_partition_number);
        Log.w(comm.PortName, $"write file legnth {getFileSize()} to partition {strPartitionStartSector}");
        long num6 = (getFileSize() + num4 - 1) / num4;
        if (num6 - num2 > num)
        {
            num6 = num2 + num;
        }
        else
        {
            num = num6 - num2;
        }
        long num7 = (getFileSize() + comm.intSectorSize - 1) / comm.intSectorSize;
        string text = string.Format(FirehoseCommands.FIREHOSE_FIRMWAREWRITE, num4, num, strPartitionStartSector, num5, pszImageFile);
        comm.SendCommand(text);
        for (long num8 = num2; num8 < num7; num8 += comm.m_dwBufferSectors)
        {
            long num9 = num7 - num8;
            num9 = ((num9 < comm.m_dwBufferSectors) ? num9 : comm.m_dwBufferSectors);
            long offset = num3 + comm.intSectorSize * num8;
            int size = (int)(comm.intSectorSize * num9);
            int n = 0;
            byte[] bytesFromFile = GetBytesFromFile(offset, size, out n);
            comm.WritePort(bytesFromFile, 0, bytesFromFile.Length);
        }
        if (chkAck)
        {
            string msg = null;
            if (!comm.chkRspAck(out msg, chunkCount.Value))
            {
                throw new Exception(msg);
            }
        }
    }

    public void WriteFile(Comm comm, string strPartitionStartSector, string strPartitionSectorNumber, string pszImageFile, string strFileStartSector, string strFileSectorOffset, string sector_size, string physical_partition_number, string addtionalFirehose, bool chkAck, int? chunkCount, int filldatasize)
    {
        long num = Convert.ToInt64(strPartitionSectorNumber);
        if (num == 0L)
        {
            num = 2147483647L;
        }
        long num2 = Convert.ToInt64(strFileStartSector);
        long num3 = Convert.ToInt64(strFileSectorOffset);
        long num4 = Convert.ToInt64(sector_size);
        long num5 = Convert.ToInt64(physical_partition_number);
        long num6 = (getFileSize() + num4 - 1) / num4;
        if (num6 - num2 > num)
        {
            num6 = num2 + num;
        }
        else
        {
            num = num6 - num2;
        }
        string command = string.Format(FirehoseCommands.FIREHOSE_PROGRAM, num4, num, strPartitionStartSector, num5, addtionalFirehose);
        comm.SendCommand(command);
        for (long num7 = num2; num7 < num6; num7 += comm.m_dwBufferSectors)
        {
            long num8 = num6 - num7;
            num8 = ((num8 < comm.m_dwBufferSectors) ? num8 : comm.m_dwBufferSectors);
            long offset = num3 + num4 * num7;
            int size = (int)(num4 * num8);
            int n = 0;
            byte[] unitBytesFromFile = GetUnitBytesFromFile(offset, size, filldatasize, out n);
            comm.WritePort(unitBytesFromFile, 0, unitBytesFromFile.Length);
        }
        if (chkAck)
        {
            string msg = null;
            if (!comm.chkRspAck(out msg, chunkCount.Value))
            {
                throw new Exception(msg);
            }
        }
    }

    public void WriteSparseFileToDevice(Comm comm, string pszPartitionStartSector, string pszPartitionSectorNumber, string pszImageFile, string pszFileStartSector, string pszSectorSizeInBytes, string pszPhysicalPartitionNumber, string addtionalFirehose)
    {
        long start_sector = Convert.ToInt32(pszPartitionStartSector);
        int partition_sector_number = Convert.ToInt32(pszPartitionSectorNumber);
        int file_start_sector = Convert.ToInt32(pszFileStartSector);
        long sparse_header_size = 0L;
        int sector_sizes_in_bytes = Convert.ToInt32(pszSectorSizeInBytes);
        Convert.ToInt32(pszPhysicalPartitionNumber);
        SparseImageHeader sparseImageHeader = default(SparseImageHeader);
        string text = "";
        if (file_start_sector != 0)
        {
            text = "ERROR_BAD_FORMAT";
            Log.w(comm.PortName, text);
        }
        if (sector_sizes_in_bytes == 0)
        {
            text = "ERROR_BAD_FORMAT";
            Log.w(comm.PortName, "ERROR_BAD_FORMAT");
        }
        int size = System.Runtime.InteropServices.Marshal.SizeOf(sparseImageHeader);
        int n = 0;
        sparseImageHeader = CommandFormat.BytesToStuct<SparseImageHeader>(GetBytesFromFile(sparse_header_size, size, out n));
        sparse_header_size += sparseImageHeader.uFileHeaderSize;
        if (sparseImageHeader.uMagic != Sparse.SPARSE_HEADER_MAGIC)
        {
            text = "ERROR_BAD_FORMAT";
            Log.w(comm.PortName, "ERROR_BAD_FORMAT " + sparseImageHeader.uMagic);
        }
        if (sparseImageHeader.uMajorVersion != Sparse.SPARSE_HEADER_MAJOR_VER)
        {
            text = "ERROR_UNSUPPORTED_TYPE";
            Log.w(comm.PortName, "ERROR_UNSUPPORTED_TYPE " + sparseImageHeader.uMajorVersion);
        }
        if (sparseImageHeader.uBlockSize % sector_sizes_in_bytes != 0L)
        {
            text = "ERROR_BAD_FORMAT";
            Log.w(comm.PortName, "ERROR_BAD_FORMAT " + sparseImageHeader.uBlockSize);
        }
        if (partition_sector_number != 0 && sparseImageHeader.uBlockSize * sparseImageHeader.uTotalBlocks / sector_sizes_in_bytes > partition_sector_number)
        {
            text = "ERROR_FILE_TOO_LARGE";
            Log.w(comm.PortName, "ERROR_FILE_TOO_LARGE size " + sparseImageHeader.uBlockSize * sparseImageHeader.uTotalBlocks / sector_sizes_in_bytes + " ullPartitionSectorNumber " + partition_sector_number);
        }
        if (!string.IsNullOrEmpty(text))
        {
            throw new Exception(text);
        }
        int num6 = 0;
        for (int i = 1; i <= sparseImageHeader.uTotalChunks; i++)
        {
            size = System.Runtime.InteropServices.Marshal.SizeOf(default(SparseChunkHeader));
            float percent = 0f;
            SparseChunkHeader sparseChunkHeader = CommandFormat.BytesToStuct<SparseChunkHeader>(GetBytesFromFile(sparse_header_size, size, out n, out percent));
            sparse_header_size += sparseImageHeader.uChunkHeaderSize;
            long num7 = sparseImageHeader.uBlockSize * sparseChunkHeader.uChunkSize;
            long num8 = num7 / sector_sizes_in_bytes;
            if (sparseChunkHeader.uChunkType == Sparse.SPARSE_CHUNK_TYPE_RAW)
            {
                if (sparseChunkHeader.uTotalSize != sparseImageHeader.uChunkHeaderSize + num7)
                {
                    Log.w(comm.PortName, "ERROR_BAD_FORMAT");
                    Log.w(comm.PortName, "vboygdi aa ERROR_BAD_FORMAT chunkHeader.uTotalSize: " + sparseChunkHeader.uTotalSize + " imageHeader.uChunkHeaderSize: " + sparseImageHeader.uChunkHeaderSize + " uChunkBytes: " + num7);
                    throw new Exception("ERROR_BAD_FORMAT");
                }
                string strPartitionStartSector = start_sector.ToString();
                string strPartitionSectorNumber = num8.ToString();
                string strFileStartSector = (sparse_header_size / sector_sizes_in_bytes).ToString();
                string strFileSectorOffset = (sparse_header_size % sector_sizes_in_bytes).ToString();
                num6++;
                bool chkAck = false;
                int value = 0;
                if (sparseImageHeader.uTotalChunks <= 10)
                {
                    if (i == sparseImageHeader.uTotalChunks)
                    {
                        value = num6;
                        chkAck = true;
                    }
                }
                else
                {
                    if (num6 % 10 == 0)
                    {
                        value = 10;
                        chkAck = true;
                    }
                    if (i == sparseImageHeader.uTotalChunks)
                    {
                        value = num6 % 10;
                        chkAck = true;
                    }
                }
                WriteFile(comm, strPartitionStartSector, strPartitionSectorNumber, pszImageFile, strFileStartSector, strFileSectorOffset, pszSectorSizeInBytes, pszPhysicalPartitionNumber, addtionalFirehose, chkAck, value);
                sparse_header_size += sector_sizes_in_bytes * num8;
                start_sector += num8;
            }
            else if (sparseChunkHeader.uChunkType == Sparse.SPARSE_CHUNK_TYPE_DONTCARE)
            {
                if (sparseChunkHeader.uTotalSize != sparseImageHeader.uChunkHeaderSize)
                {
                    Log.w(comm.PortName, "ERROR_BAD_FORMAT");
                    Log.w(comm.PortName, "vboygdi bb ERROR_BAD_FORMAT chunkHeader.uTotalSize: " + sparseChunkHeader.uTotalSize + " imageHeader.uChunkHeaderSize: " + sparseImageHeader.uChunkHeaderSize + " uChunkBytes: " + num7);
                }
                start_sector += num8;
                if (i != sparseImageHeader.uTotalChunks)
                {
                    continue;
                }
                int num9 = num6 % 10;
                if (num9 > 0)
                {
                    string msg = null;
                    if (!comm.chkRspAck(out msg, num9))
                    {
                        throw new Exception(msg);
                    }
                }
            }
            else if (sparseChunkHeader.uChunkType == Sparse.SPARSE_CHUNK_TYPE_FILL)
            {
                if (sparseChunkHeader.uTotalSize != sparseImageHeader.uChunkHeaderSize + Sparse.SPARSE_CHUNK_TYPE_FILL_DATA_SIZE)
                {
                    Log.w(comm.PortName, "ERROR_BAD_FORMAT");
                    throw new Exception("ERROR_BAD_FORMAT");
                }
                string strPartitionStartSector2 = start_sector.ToString();
                string strPartitionSectorNumber2 = num8.ToString();
                string strFileStartSector2 = (sparse_header_size / sector_sizes_in_bytes).ToString();
                string strFileSectorOffset2 = (sparse_header_size % sector_sizes_in_bytes).ToString();
                num6++;
                bool chkAck2 = false;
                int value2 = 0;
                if (sparseImageHeader.uTotalChunks <= 10)
                {
                    if (i == sparseImageHeader.uTotalChunks)
                    {
                        value2 = num6;
                        chkAck2 = true;
                    }
                }
                else
                {
                    if (num6 % 10 == 0)
                    {
                        value2 = 10;
                        chkAck2 = true;
                    }
                    if (i == sparseImageHeader.uTotalChunks)
                    {
                        value2 = num6 % 10;
                        chkAck2 = true;
                    }
                }
                WriteFile(comm, strPartitionStartSector2, strPartitionSectorNumber2, pszImageFile, strFileStartSector2, strFileSectorOffset2, pszSectorSizeInBytes, pszPhysicalPartitionNumber, addtionalFirehose, chkAck2, value2, Sparse.SPARSE_CHUNK_TYPE_FILL_DATA_SIZE);
                sparse_header_size += Sparse.SPARSE_CHUNK_TYPE_FILL_DATA_SIZE;
                start_sector += num8;
            }
            else
            {
                Log.w(comm.PortName, "ERROR_UNSUPPORTED_TYPE " + sparseChunkHeader.uChunkType);
            }
        }
    }

    public void SetBytesToFile(byte[] sendData, long offset)
    {
        if (!MemImg.isHighSpeed || MemImg.IsQuickExcluded(filePath))
        {
            try
            {
                Log.w("file size:" + fileStream.Length);
                fileStream.Seek(offset, SeekOrigin.Begin);
                fileStream.Write(sendData, 0, sendData.Length);
            }
            catch (IOException ex)
            {
                Log.w("SetBytesToFile IO err" + ex.ToString());
            }
        }
    }

    public byte[] GetBytesFromFile(long offset, int size, out int n)
    {
        float percent = 0f;
        byte[] array;
        if (MemImg.isHighSpeed && !MemImg.IsQuickExcluded(filePath))
        {
            array = MemImg.GetBytesFromFile(filePath, offset, size, out percent);
            n = array.Length;
        }
        else
        {
            long length = fileStream.Length;
            array = new byte[size];
            fileStream.Seek(offset, SeekOrigin.Begin);
            n = fileStream.Read(array, 0, size);
            percent = (float)offset / (float)length;
        }
        FlashStatus.UpdateDeviceStatus(portName, percent, null, "flashing", isDone: false);
        return array;
    }

    public byte[] GetUnitBytesFromFile(long offset, int size, int Unitsize, out int n)
    {
        float percent = 0f;
        int num = 0;
        int num2 = 0;
        byte[] array;
        if (MemImg.isHighSpeed && !MemImg.IsQuickExcluded(filePath))
        {
            array = MemImg.GetBytesFromFile(filePath, offset, size, out percent);
            n = array.Length;
        }
        else
        {
            long length = fileStream.Length;
            num = size / Unitsize;
            array = new byte[size];
            byte[] array2 = new byte[Unitsize];
            fileStream.Seek(offset, SeekOrigin.Begin);
            n = fileStream.Read(array2, 0, Unitsize);
            for (int i = 1; i <= num; i++)
            {
                Array.ConstrainedCopy(array2, 0, array, num2, Unitsize);
                num2 += Unitsize;
            }
            percent = (float)offset / (float)length;
        }
        FlashStatus.UpdateDeviceStatus(portName, percent, null, "flashing", isDone: false);
        return array;
    }

    private static string byteToHexStr(byte[] bytes, int length)
    {
        string text = "";
        if (bytes != null)
        {
            for (int i = 0; i < length; i++)
            {
                text += bytes[i].ToString("X2");
            }
        }
        return text;
    }

    public byte[] GetBytesFromFile(long offset, int size, out int n, out float percent)
    {
        byte[] array;
        if (MemImg.isHighSpeed && !MemImg.IsQuickExcluded(filePath))
        {
            array = MemImg.GetBytesFromFile(filePath, offset, size, out percent);
            n = array.Length;
        }
        else
        {
            long length = fileStream.Length;
            array = new byte[size];
            fileStream.Seek(offset, SeekOrigin.Begin);
            n = fileStream.Read(array, 0, size);
            percent = (float)offset / (float)length;
        }
        FlashStatus.UpdateDeviceStatus(portName, percent, null, "flashing", isDone: false);
        return array;
    }

    public long getFileSize()
    {
        if (fileLength != 0L)
        {
            return fileLength;
        }
        return new FileInfo(filePath).Length;
    }

    public void closeTransfer()
    {
        if (fileStream != null)
        {
            fileStream.Close();
            fileStream.Dispose();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            closeTransfer();
        }
        else if (fileStream != null)
        {
            fileStream.Close();
            fileStream.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~FileTransfer()
    {
        Dispose(disposing: false);
    }
}
