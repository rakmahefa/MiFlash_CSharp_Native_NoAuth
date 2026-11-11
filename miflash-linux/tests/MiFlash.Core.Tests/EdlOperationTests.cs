using System;
using System.IO;
using System.Linq;
using System.Text;
using MiFlash.Core.Runtime;
using MiFlash.Core.Tests.Fakes;
using MiFlash.Edl;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>
/// Tests des opérations EDL standalone du CLI (M4.1→M4.3) :
/// <see cref="EdlOperation.Backup/Restore/Provision/Reboot/RebootToEdl"/>
/// via <see cref="MultiDeviceFlasher"/> — cible simulée (RNF-5).
/// </summary>
/// <remarks>Même collection xUnit qu'EdlFlasherTests : FlashStatus.Reporter
/// est statique, ces classes ne doivent pas tourner en parallèle.</remarks>
[Collection("flash-status")]
public class EdlOperationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly System.Collections.Concurrent.ConcurrentBag<string> _status = new();

    public EdlOperationTests()
    {
        Log.Sink = static (_, _) => { };
        _tempDir = Path.Combine(Path.GetTempPath(), "miflash-ops-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        FlashStatus.Reporter = new BagReporter(_status);
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

    private static ScriptedEdlDevice NewDevice(bool quickReset = false)
    {
        var device = new ScriptedEdlDevice { ProgrammerSize = 8192, SupportsQuickReset = quickReset };
        device.Open();
        return device;
    }

    /// <summary>Paquet minimal : programmer UFS + une image + un rawprogram.</summary>
    private string CreatePackage(string name, bool withBackup = false, bool withRestore = false, bool withProvision = false)
    {
        string dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        var programmer = new byte[8192];
        Array.Fill(programmer, (byte)0x7F);
        File.WriteAllBytes(Path.Combine(dir, "prog_ufs_firehose_sm8550.elf"), programmer);
        var boot = new byte[4096];
        Array.Fill(boot, (byte)0xCD);
        File.WriteAllBytes(Path.Combine(dir, "boot.img"), boot);
        string program = "  <program filename=\"boot.img\" label=\"boot\" num_partition_sectors=\"1\"" +
                         " physical_partition_number=\"0\" start_sector=\"0\" sparse=\"false\"" +
                         " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>";
        if (withBackup)
        {
            File.WriteAllText(Path.Combine(dir, "rawprogram_backup.xml"),
                "<?xml version=\"1.0\" ?>\n<data>\n  <program filename=\"persist.img\" label=\"persist\"" +
                " num_partition_sectors=\"1\" physical_partition_number=\"0\" start_sector=\"0\"" +
                " sparse=\"false\" file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n</data>\n");
        }
        if (withRestore)
        {
            File.WriteAllText(Path.Combine(dir, "rawprogram_restore.xml"),
                "<?xml version=\"1.0\" ?>\n<data>\n" + program + "\n</data>\n");
        }
        if (withProvision)
        {
            File.WriteAllText(Path.Combine(dir, "provision.xml"),
                "<?xml version=\"1.0\" ?>\n<data>\n  <ufs LUN0=\"1\" bNumberLU=\"4\" desc=\"ignored\"/>\n</data>\n");
        }
        return dir;
    }

    private static string AllXml(ScriptedEdlDevice device)
    {
        return string.Join("\n", device.HostWrites
            .Where(w => w.Length > 5 && w[0] == (byte)'<' && w[1] == (byte)'?')
            .Select(w => Encoding.UTF8.GetString(w)));
    }

    [Fact]
    public void Backup_operation_reads_partitions_and_reports_directory()
    {
        string pkg = CreatePackage("bk-images", withBackup: true);
        var device = NewDevice();
        var job = new EdlJob
        {
            DeviceName = "9008-BK",
            SwPath = pkg,
            Operation = EdlOperation.Backup,
            TransportFactory = () => device,
        };

        var results = new MultiDeviceFlasher().Run(new[] { job });

        Assert.True(results[0].Success, results[0].ToString());
        Assert.StartsWith("backup done: ", results[0].Message);
        // La partition listée a été lue : <read> émis et image écrite dans la
        // session de sauvegarde (remplissage 0xAB de la cible simulée).
        Assert.Contains("<read ", AllXml(device));
        string backupDir = results[0].Message.Substring("backup done: ".Length);
        string persist = Path.Combine(backupDir, "persist.img");
        Assert.True(File.Exists(persist), "persist.img doit exister dans " + backupDir);
        byte[] data = File.ReadAllBytes(persist);
        Assert.Equal(0xAB, data[0]);
        Assert.True(data.Length >= 4096, "la partition lue fait au moins un bloc");
        Assert.Contains(_status, s => s.Contains("success"));
    }

    [Fact]
    public void Backup_operation_without_backup_xml_fails()
    {
        string pkg = CreatePackage("bk-none");
        var job = new EdlJob
        {
            DeviceName = "9008-BKNONE",
            SwPath = pkg,
            Operation = EdlOperation.Backup,
            TransportFactory = () => NewDevice(),
        };

        var results = new MultiDeviceFlasher().Run(new[] { job });

        Assert.False(results[0].Success);
        Assert.Contains("rawprogram_backup.xml not found", results[0].Message);
    }

    [Fact]
    public void Restore_operation_writes_images_from_backup_directory()
    {
        string pkg = CreatePackage("rs-images", withRestore: true);
        // Sauvegarde préalable : session avec boot.img (remplissage restauré).
        string backupSession = Path.Combine(_tempDir, "backup-session");
        Directory.CreateDirectory(backupSession);
        var saved = new byte[4096];
        Array.Fill(saved, (byte)0x5A);
        File.WriteAllBytes(Path.Combine(backupSession, "boot.img"), saved);

        var device = NewDevice();
        var job = new EdlJob
        {
            DeviceName = "9008-RS",
            SwPath = pkg,
            Operation = EdlOperation.Restore,
            RestoreFrom = backupSession,
            TransportFactory = () => device,
        };

        var results = new MultiDeviceFlasher().Run(new[] { job });

        Assert.True(results[0].Success, results[0].ToString());
        Assert.Equal("restore done", results[0].Message);
        // L'image de la sauvegarde a été écrite sur la cible après <program>.
        string xml = AllXml(device);
        Assert.Contains("<program", xml);
        int programIndex = device.HostWrites.ToList().FindIndex(w => w.Length > 5 && Encoding.UTF8.GetString(w).Contains("<program"));
        byte[] written = device.HostWrites[programIndex + 1];
        Assert.Equal(4096, written.Length);
        Assert.Equal((byte)0x5A, written[0]);
    }

    [Fact]
    public void Provision_operation_runs_ufs_elements_then_reconfigures()
    {
        string pkg = CreatePackage("pv-images", withProvision: true);
        var device = NewDevice(quickReset: true);
        var job = new EdlJob
        {
            DeviceName = "9008-PV",
            SwPath = pkg,
            Operation = EdlOperation.Provision,
            TransportFactory = () => device,
        };

        var results = new MultiDeviceFlasher().Run(new[] { job });

        Assert.True(results[0].Success, results[0].ToString());
        Assert.Equal("provision done", results[0].Message);
        string xml = AllXml(device);
        // Élément <ufs> renvoyé sans desc, puis partial_reset (quick_reset
        // supporté) et re-configure avec skipStorageInit=0.
        Assert.Contains("<ufs ", xml);
        Assert.DoesNotContain("desc=", xml);
        Assert.Contains("partial_reset", xml);
        Assert.Contains("SkipStorageInit=\"0\"", xml);
    }

    [Fact]
    public void Reboot_operation_sends_partial_reset()
    {
        string pkg = CreatePackage("rb-images");
        var device = NewDevice();
        var job = new EdlJob
        {
            DeviceName = "9008-RB",
            SwPath = pkg,
            Operation = EdlOperation.Reboot,
            TransportFactory = () => device,
        };

        var results = new MultiDeviceFlasher().Run(new[] { job });

        Assert.True(results[0].Success, results[0].ToString());
        Assert.Equal("reboot done", results[0].Message);
        string xml = AllXml(device);
        Assert.Contains("partial_reset", xml);
        Assert.DoesNotContain("reset_to_edl", xml);
        Assert.DoesNotContain("<program", xml);
    }

    [Fact]
    public void RebootToEdl_operation_sends_reset_to_edl()
    {
        string pkg = CreatePackage("re-images");
        var device = NewDevice();
        var job = new EdlJob
        {
            DeviceName = "9008-RE",
            SwPath = pkg,
            Operation = EdlOperation.RebootToEdl,
            TransportFactory = () => device,
        };

        var results = new MultiDeviceFlasher().Run(new[] { job });

        Assert.True(results[0].Success, results[0].ToString());
        Assert.Equal("reboot to edl done", results[0].Message);
        Assert.Contains("reset_to_edl", AllXml(device));
    }

    private sealed class BagReporter : IFlashStatusReporter
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _bag;

        public BagReporter(System.Collections.Concurrent.ConcurrentBag<string> bag) => _bag = bag;

        public void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone)
        {
            _bag.Add(result + ":" + status);
        }
    }
}
