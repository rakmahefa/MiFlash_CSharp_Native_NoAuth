using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MiFlash.Core.Runtime;
using MiFlash.Fastboot;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>
/// Tests du moteur fastboot (M5) : parsing flash_all*.bat/.sh, sélection de
/// script, exécuteur POSIX (faux binaires), flasher complet avec timeout et
/// cas Intel DNX — sans matériel (RNF-5).
/// </summary>
/// <remarks>Même collection xUnit : FlashStatus.Reporter est statique.</remarks>
[Collection("flash-status")]
public class FastbootTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConcurrentBag<string> _status = new();

    public FastbootTests()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("tests POSIX");
        }
        Log.Sink = static (_, _) => { };
        _tempDir = Path.Combine(Path.GetTempPath(), "miflash-fb-" + Path.GetRandomFileName());
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

    // ------------------------------------------------------------ ScriptEngine ----

    [Fact]
    public void Parse_bat_script_resolves_paths_and_strips_error_tails()
    {
        string rom = CreateRom(withScript: "flash_all.bat");
        var ops = ScriptEngine.Parse(Path.Combine(rom, "flash_all.bat"));

        // ping -n 3 → sleep 2, puis 4 flashes + erase + reboot, exit /B 0 stoppe.
        Assert.Equal(new[] { "sleep", "flash", "flash", "flash", "erase", "reboot" },
            ops.Select(o => o.Verb).ToArray());
        Assert.Equal(2, ops[0].SleepSeconds);
        Assert.Equal("partition", ops[1].Args.Words[0]);
        Assert.Equal(Path.Combine(rom, "images", "gpt_both0.bin").Replace('\\', '/'),
            ops[1].Args.Words[1]);
        Assert.Equal("abl", ops[2].Args.Words[0]);
        Assert.Equal("misc", ops[4].Args.Words[0]);
        // Les queues || @echo … && exit /B 1 ne polluent pas les arguments.
        Assert.DoesNotContain(ops.SelectMany(o => o.Args.Words), w => w.Contains("echo"));
        Assert.DoesNotContain(ops.SelectMany(o => o.Args.Words), w => w.Contains("exit"));
    }

    [Fact]
    public void Parse_sh_script_resolves_filepath_variable()
    {
        string rom = CreateRom(withScript: "flash_all.sh");
        var ops = ScriptEngine.Parse(Path.Combine(rom, "flash_all.sh"));

        Assert.Equal(new[] { "flash", "sleep", "reboot" }, ops.Select(o => o.Verb).ToArray());
        Assert.Equal("partition", ops[0].Args.Words[0]);
        Assert.Equal(Path.Combine(rom, "images", "gpt_both0.bin").Replace('\\', '/'),
            ops[0].Args.Words[1]);
        Assert.Equal(2, ops[1].SleepSeconds);
    }

    [Fact]
    public void Parse_empty_script_yields_no_operations()
    {
        string file = Path.Combine(_tempDir, "empty.bat");
        File.WriteAllLines(file, new[] { "@echo off", "exit /B 0" });
        Assert.Empty(ScriptEngine.Parse(file));
    }

    // ------------------------------------------------------------- FlashScript ----

    [Fact]
    public void Resolve_variants_and_sh_fallback()
    {
        string all = CreateRom(withScript: "flash_all.bat");
        Assert.EndsWith("flash_all.bat", FlashScript.Resolve(all, FlashScript.Variant.CleanAll));
        Assert.NotNull(FlashScript.Resolve(all, FlashScript.Variant.Auto));

        string sh = CreateRom(withScript: "flash_all_except_storage.sh");
        Assert.EndsWith("flash_all_except_storage.sh",
            FlashScript.Resolve(sh, FlashScript.Variant.ExceptStorage));

        string none = CreateRom(withScript: null);
        Assert.Null(FlashScript.Resolve(none, FlashScript.Variant.CleanAll));
        Assert.Null(FlashScript.Resolve(none, FlashScript.Variant.Auto));

        string exact = CreateRom(withScript: "my_custom.bat");
        Assert.EndsWith("my_custom.bat",
            FlashScript.Resolve(exact, FlashScript.Variant.Auto, explicitScript: "my_custom.bat"));
    }

    // ----------------------------------------------------------- ProcessRunner ----

    [Fact]
    public void ProcessRunner_captures_output_and_exit_code()
    {
        var runner = new ProcessRunner("fb-test");
        int code = runner.Execute("/bin/sh", "-c \"echo hello-fastboot\"");

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, runner.ErrMsg);
    }

    [Fact]
    public void ProcessRunner_detects_error_lines_like_windows_cmd()
    {
        var runner = new ProcessRunner("fb-test");
        int code = runner.Execute("/bin/sh", "-c \"echo FAILED to flash partition\"");

        Assert.Equal(0, code); // la ligne d'erreur seule ne change pas le code…
        Assert.Contains("FAILED", runner.ErrMsg); // …mais est capturée comme errMsg.
    }

    [Fact]
    public void ProcessRunner_reports_non_zero_exit()
    {
        var runner = new ProcessRunner("fb-test");
        int code = runner.Execute("/bin/false", "");

        Assert.Equal(1, code);
    }

    [Fact]
    public void ProcessRunner_timeout_kills_process()
    {
        var runner = new ProcessRunner("fb-test");
        int code = runner.Execute("/bin/sleep", "30", timeout: TimeSpan.FromMilliseconds(800));

        Assert.Equal(-1, code);
        Assert.Equal("process timeout", runner.ErrMsg);
    }

    [Fact]
    public void ProcessRunner_kills_process_tree_on_timeout()
    {
        // sh -c 'sleep 30' : le kill doit emporter l'arborescence (taskkill /T).
        var runner = new ProcessRunner("fb-test");
        int code = runner.Execute("/bin/sh", "-c \"sleep 30\"", timeout: TimeSpan.FromMilliseconds(800));

        Assert.Equal(-1, code);
    }

    // --------------------------------------------------------- FastbootFlasher ----

    [Fact]
    public void Flasher_runs_script_operations_with_serial_flag()
    {
        string rom = CreateRom(withScript: "flash_all.bat");
        string logFile = CreateFakeFastboot(rom, exitCode: 0);
        var flasher = new FastbootFlasher
        {
            FastbootPath = Path.Combine(rom, "fastboot"),
            DeviceName = "TESTSERIAL",
        };

        flasher.Flash(Path.Combine(rom, "flash_all.bat"));

        string[] invocations = File.ReadAllLines(logFile);
        Assert.Equal(5, invocations.Length); // 4 flash + reboot (sleep natif)
        Assert.All(invocations, l => Assert.StartsWith("-s TESTSERIAL ", l));
        Assert.Contains(invocations, l => l.Contains(" flash partition ") && l.EndsWith("/images/gpt_both0.bin"));
        Assert.Contains(invocations, l => l.Contains(" reboot"));
        Assert.Contains(_status, s => s.Contains("success:flash done"));
    }

    [Fact]
    public void Flasher_fails_when_fastboot_fails()
    {
        string rom = CreateRom(withScript: "flash_all.bat");
        CreateFakeFastboot(rom, exitCode: 1);
        var flasher = new FastbootFlasher
        {
            FastbootPath = Path.Combine(rom, "fastboot"),
            DeviceName = "TESTSERIAL",
        };

        var ex = Assert.Throws<Exception>(() => flasher.Flash(Path.Combine(rom, "flash_all.bat")));

        Assert.Contains("failed", ex.Message);
        Assert.Contains(_status, s => s.Contains("error"));
    }

    [Fact]
    public void Flasher_intel_dnx_boots_kerneflinger_first()
    {
        string rom = CreateRom(withScript: "flash_all.bat");
        string logFile = CreateFakeFastboot(rom, exitCode: 0);
        Directory.CreateDirectory(Path.Combine(rom, "images"));
        File.WriteAllText(Path.Combine(rom, "images", "loader.efi"), "EFI");
        var flasher = new FastbootFlasher
        {
            FastbootPath = Path.Combine(rom, "fastboot"),
            DeviceName = "TESTSERIAL",
            UsbVendorId = 0x8087,
            UsbProductId = 0x0a65,
            DeviceOnline = _ => true,
        };

        flasher.Flash(Path.Combine(rom, "flash_all.bat"));

        string[] invocations = File.ReadAllLines(logFile);
        // 1re invocation : fastboot boot loader.efi (kerneflinger).
        Assert.StartsWith("-s TESTSERIAL boot ", invocations[0]);
        Assert.EndsWith("/images/loader.efi", invocations[0]);
        Assert.Equal(6, invocations.Length);
    }

    [Fact]
    public void Flasher_without_operations_fails_cleanly()
    {
        string rom = CreateRom(withScript: null);
        string logFile = CreateFakeFastboot(rom, exitCode: 0);
        string script = Path.Combine(rom, "noop.bat");
        File.WriteAllLines(script, new[] { "@echo off", "exit /B 0" });
        var flasher = new FastbootFlasher
        {
            FastbootPath = Path.Combine(rom, "fastboot"),
            DeviceName = "TESTSERIAL",
        };

        Assert.Throws<Exception>(() => flasher.Flash(script));
    }

    // ------------------------------------------------------------- FastbootCli ----

    [Fact]
    public void LocateFastboot_finds_binary_on_path()
    {
        string bin = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "fastboot"), "#!/bin/sh\nexit 0\n");
        MakeExecutable(Path.Combine(bin, "fastboot"));
        string oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", bin);
        try
        {
            Assert.NotNull(FastbootCli.LocateFastboot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public void ListDevices_parses_fastboot_devices_output()
    {
        string rom = CreateRom(withScript: null);
        string fastboot = Path.Combine(rom, "fastboot");
        File.WriteAllText(fastboot,
            "#!/bin/sh\ncat <<'EOF'\nABCDEF012345    usb:1-3 product:raphael model:Mi_9 device:raphael\n987654FEDCBA    usb:2-1 product:davinci model:Mi_10 device:davinci\nEOF\n");
        MakeExecutable(fastboot);

        var devices = FastbootCli.ListDevices(fastboot);

        Assert.Equal(2, devices.Count);
        Assert.Equal("ABCDEF012345", devices[0].Serial);
        Assert.Equal("987654FEDCBA", devices[1].Serial);
    }

    // ------------------------------------------------------------- Fixtures ----

    private string CreateRom(string withScript)
    {
        string rom = Path.Combine(_tempDir, "rom-" + Path.GetRandomFileName());
        Directory.CreateDirectory(rom);
        if (withScript == null)
        {
            return rom;
        }
        Directory.CreateDirectory(Path.Combine(rom, "images"));
        foreach (string image in new[] { "gpt_both0.bin", "abl.elf", "boot.img" })
        {
            File.WriteAllText(Path.Combine(rom, "images", image), new string('X', 64));
        }
        if (withScript.EndsWith(".bat"))
        {
            File.WriteAllText(Path.Combine(rom, withScript),
                "@echo off\r\n" +
                "set \"filepath=%~dp0\"\r\n" +
                "title flash all\r\n" +
                "ping -n 3 127.0.0.1>nul\r\n" +
                "fastboot %* flash partition \"%filepath%images\\gpt_both0.bin\" || @echo \"Flash gpt error\" && exit /B 1\r\n" +
                "fastboot %* flash abl \"%filepath%images\\abl.elf\" || @echo \"Flash abl error\" && exit /B 1\r\n" +
                "fastboot %* flash boot \"%filepath%images\\boot.img\" || @echo \"Flash boot error\" && exit /B 1\r\n" +
                "fastboot %* erase misc || @echo \"Erase misc error\" && exit /B 1\r\n" +
                "fastboot %* reboot || @echo \"Reboot error\" && exit /B 1\r\n" +
                "exit /B 0\r\n");
        }
        else
        {
            File.WriteAllText(Path.Combine(rom, withScript),
                "#!/bin/sh\n" +
                "filepath=$(cd \"$(dirname \"$0\")\"; pwd -P)\n" +
                "fastboot $* flash partition \"$filepath/images/gpt_both0.bin\" || exit 1\n" +
                "sleep 2\n" +
                "fastboot $* reboot || exit 1\n" +
                "exit 0\n");
        }
        return rom;
    }

    /// <summary>Faux binaire fastboot : journalise ses arguments et sort exitCode.</summary>
    private string CreateFakeFastboot(string rom, int exitCode)
    {
        string logFile = Path.Combine(rom, "fastboot-calls.log");
        File.WriteAllText(Path.Combine(rom, "fastboot"),
            "#!/bin/sh\necho \"$@\" >> \"" + logFile + "\"\nexit " + exitCode + "\n");
        MakeExecutable(Path.Combine(rom, "fastboot"));
        return logFile;
    }

    private static void MakeExecutable(string path)
    {
        // CA1416 : UnixFileMode n'existe que sur POSIX — ces tests sont
        // eux-mêmes limités à Linux (cf. constructeur).
#pragma warning disable CA1416
        var info = new FileInfo(path);
        info.UnixFileMode |= System.IO.UnixFileMode.UserExecute
                             | System.IO.UnixFileMode.GroupExecute
                             | System.IO.UnixFileMode.OtherExecute;
#pragma warning restore CA1416
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
