using System;
using System.Collections.Concurrent;
using System.IO;
using MiFlash.Core.Runtime;
using MiFlash.Core.Tests.Fakes;
using MiFlash.Edl;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>
/// Tests de l'orchestrateur multi-appareils (M3.9) : une Task par appareil,
/// résultats agrégés, un échec n'arrête pas les autres (RNF-5, sans matériel).
/// </summary>
/// <remarks>Même collection xUnit qu'EdlFlasherTests : FlashStatus.Reporter
/// est statique, ces classes ne doivent pas tourner en parallèle.</remarks>
[Collection("flash-status")]
public class MultiDeviceFlasherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConcurrentBag<string> _status = new();

    public MultiDeviceFlasherTests()
    {
        Log.Sink = static (_, _) => { };
        _tempDir = Path.Combine(Path.GetTempPath(), "miflash-multi-" + Path.GetRandomFileName());
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

    private static ScriptedEdlDevice NewDevice(int programmerSize = 8192)
    {
        var device = new ScriptedEdlDevice { ProgrammerSize = programmerSize };
        device.Open();
        return device;
    }

    private string CreatePackage(string name)
    {
        string dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        var programmer = new byte[8192];
        Array.Fill(programmer, (byte)0x7F);
        File.WriteAllBytes(Path.Combine(dir, "prog_ufs_firehose_sm8550.elf"), programmer);
        var boot = new byte[4096];
        Array.Fill(boot, (byte)0xCD);
        File.WriteAllBytes(Path.Combine(dir, "boot.img"), boot);
        File.WriteAllText(Path.Combine(dir, "rawprogram0.xml"),
            "<?xml version=\"1.0\" ?>\n<data>\n" +
            "  <program filename=\"boot.img\" label=\"boot\" num_partition_sectors=\"1\"" +
            " physical_partition_number=\"0\" start_sector=\"0\" sparse=\"false\"" +
            " file_sector_offset=\"0\" sector_size_in_bytes=\"4096\"/>\n" +
            "</data>\n");
        File.WriteAllText(Path.Combine(dir, "patch0.xml"),
            "<?xml version=\"1.0\" ?>\n<patches>\n" +
            "  <patch filename=\"DISK\" sector_size_in_bytes=\"4096\" byte_offset=\"4\"" +
            " physical_partition_number=\"0\" size_in_bytes=\"4\" start_sector=\"0\" value=\"0x11223344\" what=\"Update\"/>\n" +
            "</patches>\n");
        return dir;
    }

    [Fact]
    public void Runs_jobs_in_parallel_and_reports_all_results()
    {
        string pkgA = CreatePackage("dev-a-images");
        string pkgB = CreatePackage("dev-b-images");
        var runner = new MultiDeviceFlasher { MaxDegreeOfParallelism = 2 };
        var jobs = new[]
        {
            new EdlJob { DeviceName = "9008-A", SwPath = pkgA, TransportFactory = () => NewDevice() },
            new EdlJob { DeviceName = "9008-B", SwPath = pkgB, TransportFactory = () => NewDevice() },
        };

        var results = runner.Run(jobs);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success, r.ToString()));
        Assert.Equal("9008-A", results[0].DeviceName);
        Assert.Equal("9008-B", results[1].DeviceName);
        // Chaque appareil a bien atteint le statut final (FlashStatus partagé).
        Assert.Contains(_status, s => s.Contains("success:flash done"));
    }

    [Fact]
    public void Failing_job_does_not_stop_others()
    {
        string pkgOk = CreatePackage("ok-images");
        string pkgMissing = Path.Combine(_tempDir, "does-not-exist");
        var runner = new MultiDeviceFlasher();
        var jobs = new[]
        {
            new EdlJob { DeviceName = "9008-GOOD", SwPath = pkgOk, TransportFactory = () => NewDevice() },
            new EdlJob { DeviceName = "9008-BAD", SwPath = pkgMissing, TransportFactory = () => NewDevice() },
        };

        var results = runner.Run(jobs);

        Assert.True(results[0].Success, results[0].ToString());
        Assert.False(results[1].Success);
        Assert.Equal("sw path is not valid", results[1].Message);
        Assert.NotNull(results[1].Error);
    }

    [Fact]
    public void Run_without_jobs_returns_empty()
    {
        var runner = new MultiDeviceFlasher();
        Assert.Empty(runner.Run(Array.Empty<EdlJob>()));
    }

    private sealed class BagReporter : IFlashStatusReporter
    {
        private readonly ConcurrentBag<string> _bag;

        public BagReporter(ConcurrentBag<string> bag) => _bag = bag;

        public void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone)
        {
            _bag.Add(result + ":" + status);
        }
    }
}
