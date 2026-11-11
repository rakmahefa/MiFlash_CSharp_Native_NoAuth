using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MiFlash.Core.Auth;
using MiFlash.Core.Firehose;
using MiFlash.Core.Protocol;
using MiFlash.Core.Runtime;
using MiFlash.Core.Tests.Fakes;
using MiFlash.Edl;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>Collection xUnit partagée avec MultiDeviceFlasherTests :
/// FlashStatus.Reporter est statique — ces classes ne doivent pas tourner
/// en parallèle (elles se l'échangeraient en cours de test).</summary>
[Collection("flash-status")]
public class EdlFlasherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _statusLog = new();

    public EdlFlasherTests()
    {
        Log.Sink = static (_, _) => { };
        _tempDir = Path.Combine(Path.GetTempPath(), "miflash-edl-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        FlashStatus.Reporter = new CaptureReporter(_statusLog);
    }

    public void Dispose()
    {
        FlashStatus.Reporter = null;
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class CaptureReporter : IFlashStatusReporter
    {
        private readonly List<string> _log;

        public CaptureReporter(List<string> log) => _log = log;

        public void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone)
        {
            if (!string.IsNullOrEmpty(status))
            {
                _log.Add(result + ":" + status);
            }
        }
    }

    private string CreateImage(string name, int size, byte fill = 0xAB)
    {
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(fill + i % 7);
        }
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private (EdlFlasher Flasher, ScriptedEdlDevice Device) CreateFlasher(string programmerPattern = "generic")
    {
        var device = new ScriptedEdlDevice();
        device.Open();
        var flasher = new EdlFlasher(device.PortName, _tempDir, device);
        return (flasher, device);
    }

    [Fact]
    public void Full_flash_flow_sahara_configure_peek_ddr_rawprogram_patch_reset()
    {
        // Paquet d'images : programmers DDR4+DDR5 (déclenche le peek), rawprogram/patch.
        byte[] programmerDdr4 = File.ReadAllBytes(CreateImage("prog_ufs_firehose_sm8250_ddr_4.elf", 8192, 0x7F));
        byte[] programmerDdr5 = File.ReadAllBytes(CreateImage("prog_ufs_firehose_sm8250_ddr_5.elf", 8192, 0x7E));
        CreateImage("boot.img", 4096, 0xCD);
        File.WriteAllText(Path.Combine(_tempDir, "rawprogram0.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <program filename=\"boot.img\" label=\"boot\" num_partition_sectors=\"1\"" +
            " physical_partition_number=\"0\" start_sector=\"0\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n" +
            "</data>\n");
        File.WriteAllText(Path.Combine(_tempDir, "patch0.xml"),
            "<?xml version=\"1.0\" ?>\n<patches>\n" +
            "  <patch filename=\"DISK\" sector_size_in_bytes=\"4096\" byte_offset=\"4\"" +
            " physical_partition_number=\"0\" size_in_bytes=\"4\" start_sector=\"0\" value=\"0x11223344\" what=\"Update\"/>\n" +
            "</patches>\n");

        (EdlFlasher flasher, ScriptedEdlDevice device) = CreateFlasher();
        device.ProgrammerSize = 8192;

        flasher.Flash();

        // Stockage détecté depuis le nom du programmer.
        Assert.Equal("ufs", flasher.storageType);
        Assert.True(flasher.comm.isDdr4, "le peek doit détecter DDR4");
        Assert.Equal(FirehoseCommands.PROGRAMMER_DDR4, flasher.programmerName);

        // 1re écriture hôte = HELLO RESPONSE 48 octets (commande 2).
        Assert.Equal(48, device.HostWrites[0].Length);
        Assert.Equal(2u, BitConverter.ToUInt32(device.HostWrites[0], 0));

        // Le programmer complet a été transféré par blocs READ DATA : deux passes
        // (motif générique, puis DDR4 après le peek). L'ordre GetFiles() n'étant
        // pas garanti, la 1re passe peut prendre ddr_4 ou ddr_5 ; la 2e doit
        // être le programmer DDR4 détecté par le peek CPUID.
        int programIndex = device.HostWrites.ToList().FindIndex(w => w.Length > 5 && w[0] == (byte)'<' && Encoding.UTF8.GetString(w).Contains("<program"));
        var chunkWrites = device.HostWrites.Take(programIndex).Where(w => w.Length == 4096).ToList();
        Assert.Equal(4, chunkWrites.Count); // 2 passes × 2 blocs de 4096
        byte[] firstPass = chunkWrites.Take(2).SelectMany(w => w).ToArray();
        Assert.Equal(8192, firstPass.Length);
        Assert.True(firstPass.SequenceEqual(programmerDdr4) || firstPass.SequenceEqual(programmerDdr5),
            "la 1re passe doit transférer l'un des programmers du motif générique");
        byte[] secondPass = chunkWrites.Skip(2).SelectMany(w => w).ToArray();
        Assert.Equal(programmerDdr4, secondPass);

        // Commandes XML envoyées : nop, configure, peek, configure (2e passe),
        // setbootablestoragedrive, program, patch, power partial_reset.
        string allXml = string.Join("\n", device.HostWrites
            .Where(w => w.Length > 5 && w[0] == (byte)'<' && w[1] == (byte)'?')
            .Select(w => Encoding.UTF8.GetString(w)));
        Assert.Contains("<nop", allXml);
        Assert.Contains("<configure", allXml);
        Assert.Contains("<peek", allXml);
        Assert.Contains("<setbootablestoragedrive", allXml);
        Assert.Contains("<program", allXml);
        Assert.Contains("num_partition_sectors=\"1\"", allXml);
        Assert.Contains("<patch", allXml);
        Assert.Contains("partial_reset", allXml);

        // Les données de boot.img ont été écrites après la commande program.
        byte[] bootData = device.HostWrites[programIndex + 1];
        Assert.Equal(4096, bootData.Length);
        Assert.Equal(0xCD, bootData[0]);

        // Statut final.
        Assert.Contains(_statusLog, s => s == "success:flash done");
    }

    [Fact]
    public void Flash_opens_the_transport_if_the_pipeline_left_it_closed()
    {
        // Régression v1.0.1 (retour matériel POCO F3) : le pipeline CLI/GUI
        // (EdlTargets -> MultiDeviceFlasher) fabrique un transport NON ouvert ;
        // BeginSession doit l'ouvrir avant Sahara, sinon « port ... is not
        // open » dès le premier flash réel. Les autres tests appelaient
        // device.Open() avant, masquant le défaut.
        CreateImage("prog_ufs_firehose_sm8250_ddr_5.elf", 8192, 0x7F);
        CreateImage("boot.img", 4096, 0xCD);
        File.WriteAllText(Path.Combine(_tempDir, "rawprogram0.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <program filename=\"boot.img\" label=\"boot\" num_partition_sectors=\"1\"" +
            " physical_partition_number=\"0\" start_sector=\"0\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n" +
            "</data>\n");
        File.WriteAllText(Path.Combine(_tempDir, "patch0.xml"),
            "<?xml version=\"1.0\" ?>\n<patches>\n" +
            "  <patch filename=\"DISK\" sector_size_in_bytes=\"4096\" byte_offset=\"4\"" +
            " physical_partition_number=\"0\" size_in_bytes=\"4\" start_sector=\"0\" value=\"0x11223344\" what=\"Update\"/>\n" +
            "</patches>\n");

        var device = new ScriptedEdlDevice { ProgrammerSize = 8192 }; // PAS d'Open()
        var flasher = new EdlFlasher(device.PortName, _tempDir, device);

        flasher.Flash(); // sans correctif : « port SIM9008 is not open. »

        // Le flux a bien atteint Sahara puis firehose jusqu'au reset final.
        Assert.NotEmpty(device.HostWrites);
        Assert.Equal(48, device.HostWrites[0].Length); // HELLO RESPONSE
        Assert.Equal(2u, BitConverter.ToUInt32(device.HostWrites[0], 0));
        string allXml = string.Join("\n", device.HostWrites
            .Where(w => w.Length > 5 && w[0] == (byte)'<' && w[1] == (byte)'?')
            .Select(w => Encoding.UTF8.GetString(w)));
        Assert.Contains("<program", allXml);
        Assert.Contains("partial_reset", allXml);
        Assert.Contains(_statusLog, s => s == "success:flash done");
    }

    [Fact]
    public void Sahara_recovers_from_consumed_hello_via_usb_port_reset()
    {
        // Régression v1.0.2 (retour matériel POCO F3) : sous Linux, qcserial ou
        // qcaux peut binder l'interface 9008 et consommer le HELLO Sahara
        // initial AVANT le claim libusb — plus aucune donnée n'arrive jamais
        // et la lecture bloquait indéfiniment (peek « timeout 0 » = illimité
        // côté libusb). La boucle de récupération doit resetter le port USB :
        // le PBL relance Sahara, renvoie un HELLO frais, et le téléversement
        // du programmer se poursuit normalement.
        byte[] programmer = File.ReadAllBytes(CreateImage("prog_ufs_firehose_sm8250_ddr_4.elf", 8192, 0x7F));
        (EdlFlasher flasher, ScriptedEdlDevice device) = CreateFlasher();
        device.ResendHelloOnReset = true;
        device.DiscardBuffers(); // HELLO initial consommé par le driver noyau

        flasher.SaharaDownloadProgrammer(FirehoseCommands.PROGRAMMER_DDR4);

        // Un reset USB a été demandé pendant la récupération.
        Assert.Equal(1, device.ResetPortCount);
        Assert.Contains(_statusLog, s => s.Contains("reset USB port"));
        Assert.Contains(_statusLog, s => s.Contains("received hello packet"));

        // Séquence de récupération : HELLO RESPONSE mode 3 (« switch mode
        // back ») puis SWITCH_MODE, avant le HELLO RESPONSE nominal (mode 0).
        Assert.Equal(2u, BitConverter.ToUInt32(device.HostWrites[0], 0));
        Assert.Equal(3u, BitConverter.ToUInt32(device.HostWrites[0], 20));
        Assert.Equal(12u, BitConverter.ToUInt32(device.HostWrites[1], 0));
        Assert.Equal(2u, BitConverter.ToUInt32(device.HostWrites[2], 0));
        Assert.Equal(0u, BitConverter.ToUInt32(device.HostWrites[2], 20));

        // Le programmer complet a été téléversé après la récupération.
        byte[] transferred = device.HostWrites.Where(w => w.Length == 4096).SelectMany(w => w).ToArray();
        Assert.Equal(programmer, transferred);
        Assert.Contains(_statusLog, s => s.Contains("transferred successfully"));
    }

    [Fact]
    public void DlAuth_sends_authp_then_256_byte_blob()
    {
        var device = new ScriptedEdlDevice();
        device.Open();
        var flasher = new EdlFlasher(device.PortName, _tempDir, device);

        // Drainer le HELLO initial (consommé par Sahara dans le flux réel).
        var sink = new byte[64];
        device.Read(sink, 0, device.BytesToRead);

        bool ok = flasher.DlAuth();

        Assert.True(ok);
        // AUTHP avec la taille annoncée, puis le blob brut.
        string authp = Encoding.UTF8.GetString(device.HostWrites[0]);
        Assert.Contains("<sig TargetName=\"sig\"", authp);
        Assert.Contains("size_in_bytes=\"256\"", authp);
        byte[] blob = device.HostWrites[1];
        Assert.Equal(256, blob.Length);
        Assert.Equal(0xBF, blob[0]);
        Assert.Equal(0x91, blob[^1]);
    }

    [Fact]
    public void ConfigureDDR_falls_back_to_auth_when_needed()
    {
        // La cible refuse le 1er configure et exige l'auth (NoAuth).
        var device = new RejectingAuthDevice();
        device.Open();
        var flasher = new EdlFlasher(device.PortName, _tempDir, device);
        flasher.comm.intSectorSize = 4096;
        flasher.comm.responsePolls = 2; // NAK : pas besoin des 32 polls fidèles (~16 s)

        // Drainer le HELLO initial (consommé par Sahara dans le flux réel).
        var sink = new byte[64];
        device.Read(sink, 0, device.BytesToRead);

        flasher.ConfigureDDR(4096, 256, "ufs", 0);

        // Après le cycle AUTHP + blob + reconfigure, le 2e configure passe.
        Assert.Equal(2, device.ConfigureAttempts);
        Assert.True(device.BlobReceived.Length == 256);
    }

    [Fact]
    public void Sahara_fails_cleanly_without_programmer()
    {
        (EdlFlasher flasher, _) = CreateFlasher();
        Assert.Throws<System.Exception>(() => flasher.SaharaDownloadProgrammer(0));
    }

    [Fact]
    public void Provision_backup_restore_full_flow()
    {
        // M3.4/M3.5 : paquet avec provision.xml, rawprogram_backup.xml et
        // rawprogram_restore.xml. La cible annonce quick_reset (partial_reset).
        CreateImage("prog_ufs_firehose_sm8550.elf", 8192, 0x7F);
        CreateImage("boot.img", 4096, 0xCD);
        File.WriteAllText(Path.Combine(_tempDir, "rawprogram0.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <program filename=\"boot.img\" label=\"boot\" num_partition_sectors=\"1\"" +
            " physical_partition_number=\"0\" start_sector=\"0\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n" +
            "</data>\n");
        File.WriteAllText(Path.Combine(_tempDir, "patch0.xml"),
            "<?xml version=\"1.0\" ?>\n<patches>\n" +
            "  <patch filename=\"DISK\" sector_size_in_bytes=\"4096\" byte_offset=\"4\"" +
            " physical_partition_number=\"0\" size_in_bytes=\"4\" start_sector=\"0\" value=\"0x11223344\" what=\"Update\"/>\n" +
            "</patches>\n");
        File.WriteAllText(Path.Combine(_tempDir, "provision.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <ufs LUNum=\"0\" bBootEn=\"0\" bSize=\"0\" bMemType=\"0\" desc=\"LU0\"/>\n" +
            "  <ufs LUNum=\"1\" bBootEn=\"0\" bSize=\"0\" bMemType=\"2\" desc=\"LU1 write boosted\"/>\n" +
            "</data>\n");
        File.WriteAllText(Path.Combine(_tempDir, "rawprogram_backup.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <program filename=\"persist.img\" label=\"persist\" num_partition_sectors=\"1\"" +
            " physical_partition_number=\"0\" start_sector=\"1024\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\" size_in_kb=\"4\"/>\n" +
            "</data>\n");
        File.WriteAllText(Path.Combine(_tempDir, "rawprogram_restore.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <program filename=\"persist.img\" label=\"persist\" num_partition_sectors=\"1\"" +
            " physical_partition_number=\"0\" start_sector=\"1024\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n" +
            "</data>\n");

        var device = new ScriptedEdlDevice { ProgrammerSize = 8192, SupportsQuickReset = true, ReadDataSize = 4096, ReadDataFill = 0xAB };
        device.Open();
        string xdgRoot = Path.Combine(_tempDir, "xdg-data");
        var flasher = new EdlFlasher(device.PortName, _tempDir, device) { RestoreRoot = xdgRoot };

        flasher.Flash();

        // M3.4 — la sauvegarde a été écrite dans le répertoire XDG de session.
        string backupFile = Path.Combine(flasher.RestoreDirectory, "persist.img");
        Assert.True(File.Exists(backupFile), "persist.img doit être sauvegardé : " + backupFile);
        byte[] backed = File.ReadAllBytes(backupFile);
        Assert.Equal(4096, backed.Length);
        Assert.All(backed, b => Assert.Equal(0xAB, b));

        string allXml = string.Join("\n", device.HostWrites
            .Where(w => w.Length > 5 && w[0] == (byte)'<' && w[1] == (byte)'?')
            .Select(w => Encoding.UTF8.GetString(w)));

        // M3.5 — chaque <ufs> est renvoyé sans desc, avec verbose (défaut).
        Assert.Equal(2, allXml.Split("<ufs ").Length - 1);
        Assert.Contains("LUNum=\"0\"", allXml);
        Assert.Contains("bMemType=\"2\"", allXml);
        Assert.DoesNotContain("desc=", allXml);
        Assert.Contains("verbose=\"1\"", allXml);
        // Provision → partial_reset (quick_reset) → re-configure avec
        // SkipStorageInit=0 après le provision.
        Assert.Contains("SkipStorageInit=\"1\"", allXml);
        Assert.Contains("SkipStorageInit=\"0\"", allXml);
        int partialResets = allXml.Split("partial_reset").Length - 1;
        Assert.True(partialResets >= 2, "partial_reset attendu après provision ET en fin de flash");

        // M3.4 — restauration : rawprogram0 (boot.img 0xCD) + restore
        // (persist.img 0xAB depuis le répertoire de sauvegarde).
        Assert.Equal(2, allXml.Split("<program ").Length - 1);
        byte[][] dataWrites = device.HostWrites.Where(w => w.Length > 5 && w[0] != (byte)'<' && w.Length % 4096 == 0 && w.Length >= 4096 && !LooksLikeSahara(w)).ToArray();
        Assert.Contains(dataWrites, w => w[0] == 0xAB && w.All(b => b == 0xAB));
        Assert.Contains(dataWrites, w => w[0] == 0xCD);

        // Statuts : provisioning vu, flash final en succès.
        Assert.Contains(_statusLog, s => s.Contains("provisioning:"));
        Assert.Contains(_statusLog, s => s == "success:flash done");
    }

    [Fact]
    public void Firmwarewrite_matches_ffu_by_storageinfo_and_writes_file()
    {
        // M3.8 — GETSTORAGEINFO → inquiry UFS → FFU correspondant écrit.
        var device = new ScriptedEdlDevice();
        device.Open();
        DrainHello(device);
        var flasher = new EdlFlasher(device.PortName, _tempDir, device) { FfuDirectory = _tempDir };
        // État post-Sahara/post-configure (ici sautés car test unitaire).
        flasher.comm.intSectorSize = 4096;
        flasher.comm.m_dwBufferSectors = 256;
        CreateImage("ffu_test.bin", 2048, 0x5A);
        flasher.FfuList.Add(new Models.Ffu
        {
            Name = "MNVNNX4Q",
            Version = "1ABCDEFGHIJKLMNO",
            Number = "1234",
            File = "ffu_test.bin",
        });

        bool ok = flasher.Firmwarewrite();

        Assert.True(ok);
        string allXml = string.Join("\n", device.HostWrites
            .Where(w => w.Length > 5 && w[0] == (byte)'<' && w[1] == (byte)'?')
            .Select(w => Encoding.UTF8.GetString(w)));
        Assert.Contains("<getStorageInfo", allXml);
        Assert.Contains("<firmwarewrite", allXml);
        // Écriture FFU complétée au secteur plein : données du fichier + padding.
        byte[] expected = File.ReadAllBytes(Path.Combine(_tempDir, "ffu_test.bin"));
        byte[] ffuData = device.HostWrites.FirstOrDefault(w => w.Length == 4096 && w[0] == 0x5A);
        Assert.NotNull(ffuData);
        Assert.Equal(expected, ffuData.Take(expected.Length).ToArray());
    }

    [Fact]
    public void Firmwarewrite_without_match_skips_write()
    {
        var device = new ScriptedEdlDevice();
        device.Open();
        DrainHello(device);
        var flasher = new EdlFlasher(device.PortName, _tempDir, device) { FfuDirectory = _tempDir };
        flasher.comm.intSectorSize = 4096; // normalement posé par Sahara (4096 UFS)
        flasher.comm.m_dwBufferSectors = 256;
        flasher.FfuList.Add(new Models.Ffu
        {
            Name = "ZZZZZZZZ",
            Version = "unmatched00000000",
            Number = "9999",
            File = "nope.bin",
        });

        bool ok = flasher.Firmwarewrite();

        Assert.True(ok);
        Assert.DoesNotContain(device.HostWrites, w => w.Length > 5 && w[0] == (byte)'<'
            && Encoding.UTF8.GetString(w).Contains("<firmwarewrite"));
    }

    [Fact]
    public void GetSha256Digest_requests_digest_per_program()
    {
        CreateImage("boot.img", 4096, 0xCD);
        CreateImage("system.img", 8192, 0xCE);
        File.WriteAllText(Path.Combine(_tempDir, "rawprogram0.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <program filename=\"boot.img\" label=\"boot\" num_partition_sectors=\"1\"" +
            " physical_partition_number=\"0\" start_sector=\"0\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n" +
            "  <program filename=\"system.img\" label=\"system\" num_partition_sectors=\"2\"" +
            " physical_partition_number=\"1\" start_sector=\"64\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n" +
            "</data>\n");

        var device = new ScriptedEdlDevice();
        device.Open();
        DrainHello(device);
        var flasher = new EdlFlasher(device.PortName, _tempDir, device);

        flasher.GetSha256Digest(_tempDir, Path.Combine(_tempDir, "rawprogram0.xml"));

        string allXml = string.Join("\n", device.HostWrites
            .Where(w => w.Length > 5 && w[0] == (byte)'<' && w[1] == (byte)'?')
            .Select(w => Encoding.UTF8.GetString(w)));
        Assert.Equal(2, allXml.Split("<getsha256digest").Length - 1);
        Assert.Contains("SECTOR_SIZE_IN_BYTES=\"4096\"", allXml);
        Assert.Contains("start_sector=\"64\"", allXml);
        Assert.Contains("physical_partition_number=\"1\"", allXml);
    }

    /// <summary>Écarte les paquets structurés Sahara (commande ≠ 0) des
    /// écritures de données images.</summary>
    private static bool LooksLikeSahara(byte[] write)
    {
        // Les données images commencent par le contenu du fichier ; les
        // paquets Sahara commencent par une commande uint32 petite (1..7).
        uint cmd = BitConverter.ToUInt32(write, 0);
        return cmd is >= 1 and <= 7;
    }

    /// <summary>Vide le HELLO initial du simulateur (consommé par Sahara
    /// dans le flux réel — sinon les commandes autonomes le parsent).</summary>
    private static void DrainHello(ScriptedEdlDevice device)
    {
        var sink = new byte[64];
        device.Read(sink, 0, device.BytesToRead);
    }

    [Fact]
    public void CekSparse_detects_sparse_magic()
    {
        string raw = CreateImage("raw.bin", 4096);
        Assert.False(EdlFlasher.CekSparse(raw));

        var header = new Images.SparseImageHeader
        {
            uMagic = Images.Sparse.SPARSE_HEADER_MAGIC,
            uMajorVersion = 1,
            uFileHeaderSize = 28,
            uChunkHeaderSize = 12,
            uBlockSize = 4096,
        };
        string sparse = Path.Combine(_tempDir, "sparse.bin");
        File.WriteAllBytes(sparse, Protocol.CommandFormat.StructToBytes(header, 28));
        Assert.True(EdlFlasher.CekSparse(sparse));
    }

    /// <summary>Cible qui rejette le premier configure (auth EDL exigée).</summary>
    private sealed class RejectingAuthDevice : ScriptedEdlDevice
    {
        public int ConfigureAttempts { get; private set; }

        public byte[] BlobReceived { get; private set; } = System.Array.Empty<byte>();

        public override void Write(byte[] buffer, int offset, int count)
        {
            var chunk = new byte[count];
            System.Array.Copy(buffer, offset, chunk, 0, count);
            if (count > 5 && chunk[0] == (byte)'<' && chunk[1] == (byte)'?')
            {
                string xml = Encoding.UTF8.GetString(chunk);
                if (xml.Contains("<configure"))
                {
                    ConfigureAttempts++;
                    if (ConfigureAttempts == 1)
                    {
                        EnqueueToHostText(
                            "<?xml version=\"1.0\" ?><data><response value=\"NAK\" rawmode=\"false\"/>" +
                            "<log value=\"error: only nop and sig tag can be recevied before authentication\"/></data>");
                        return;
                    }
                    EnqueueToHostText("<?xml version=\"1.0\" ?><data><response value=\"ACK\" MaxPayloadSizeToTargetInBytes=\"1048576\"/></data>");
                    return;
                }
                if (xml.Contains("<sig"))
                {
                    EnqueueToHostText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
                    return;
                }
                EnqueueToHostText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
                return;
            }
            if (ConfigureAttempts == 1 && BlobReceived.Length == 0 && count == 256)
            {
                BlobReceived = chunk;
                EnqueueToHostText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
                return;
            }
            base.Write(buffer, offset, count);
        }
    }
}
