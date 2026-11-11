using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiFlash.Cli.Io;
using MiFlash.Cli.i18n;
using MiFlash.Cli.Ui;
using MiFlash.Core.Runtime;
using MiFlash.Fastboot;
using MiFlash.Transport.Usb;
using Spectre.Console;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash flash-fastboot &lt;rom&gt; [--script auto|all|except-storage|lock|&lt;fichier&gt;]</c>
/// (M5.6, RF-12→RF-15) — flash d'une ROM fastboot via le moteur de scripts
/// natif (parsing flash_all*.bat/.sh, timeout 11 min 40 s, Intel DNX).
/// Cibles : appareils « fastboot devices » (filtrés par --device, défaut :
/// tous) ; VID/PID récupérés par corrélation libusb pour le cas DNX.
/// </summary>
internal static class FastbootCommand
{
    public static Command Build()
    {
        Argument<DirectoryInfo> rom = new("rom")
        {
            Description = "fastboot ROM directory (flash_all*.bat/.sh + images/)",
        };
        Option<string> script = new("--script", new[] { "-S" })
        {
            Description = "flash script: auto (default), all, except-storage, lock, or an exact file name",
            DefaultValueFactory = _ => "auto",
        };
        Option<List<string>> device = EdlCli.DeviceOption();
        Option<bool> yes = EdlCli.YesOption();

        var command = new Command("flash-fastboot", "flash a fastboot ROM via flash_all script (native engine)");
        command.Add(rom);
        command.Add(script);
        command.Add(device);
        command.Add(yes);
        command.SetAction(parseResult => Run(
            parseResult.GetValue(rom),
            parseResult.GetValue(script),
            parseResult.GetValue(device),
            parseResult.GetValue(yes)));
        return command;
    }

    private static int Run(DirectoryInfo romDir, string scriptChoice, List<string> deviceSelectors, bool yes)
    {
        string romPath = romDir?.FullName ?? ".";
        if (!System.IO.Directory.Exists(romPath))
        {
            Console.Error.WriteLine("error: directory not found: " + romPath);
            return 2;
        }

        string fastbootPath = FastbootCli.LocateFastboot();
        if (fastbootPath == null)
        {
            AnsiConsole.MarkupLine("[red]fastboot binary not found on PATH — install 'fastboot' / 'android-tools' (see 'miflash doctor')[/]");
            return 4;
        }

        // Cibles : fastboot devices (+ VID/PID par corrélation libusb).
        List<(string Serial, string Description)> fastbootDevices = FastbootCli.ListDevices(fastbootPath);
        if (fastbootDevices.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]" + EdlTargets.Escape(L._("Cli.NoDevice")) + "[/]");
            return 3;
        }
        Dictionary<string, UsbDeviceIdentity> usbBySerial;
        using (var enumerator = new UsbDeviceEnumerator())
        {
            usbBySerial = FastbootCli.CorrelateWithUsb(enumerator.ListDevices());
        }
        List<string> targets = fastbootDevices.Select(d => d.Serial).ToList();
        if (deviceSelectors is { Count: > 0 })
        {
            targets = targets.Where(t => deviceSelectors.Any(s =>
                string.Equals(s, t, StringComparison.OrdinalIgnoreCase))).ToList();
            if (targets.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]" + EdlTargets.Escape(L._("Cli.DeviceNotFound"))
                    + string.Join(", ", deviceSelectors) + "[/]");
                return 3;
            }
        }

        // Script.
        (FlashScript.Variant variant, string explicitScript) = ParseScriptChoice(scriptChoice);
        string scriptPath = FlashScript.Resolve(romPath, variant, explicitScript);
        if (scriptPath == null)
        {
            AnsiConsole.MarkupLine($"[red]flash script not found in {EdlTargets.Escape(romPath)} (script: {EdlTargets.Escape(scriptChoice)})[/]");
            return 2;
        }

        // Confirmation (M4.4) : opération destructive.
        if (!Confirm(yes, targets, scriptPath))
        {
            AnsiConsole.MarkupLine("[yellow]" + EdlTargets.Escape(L._("Cli.Aborted")) + "[/]");
            return 1;
        }

        Log.Sink = SessionLog.Write;
        using var reporter = new ConsoleReporter();
        FlashStatus.Reporter = reporter;

        AnsiConsole.MarkupLine($"[grey]fastboot: {EdlTargets.Escape(fastbootPath)}[/]");
        AnsiConsole.MarkupLine($"[grey]script: {EdlTargets.Escape(scriptPath)}[/]");

        var tasks = targets.Select(serial => Task.Run(() =>
        {
            var flasher = new FastbootFlasher
            {
                FastbootPath = fastbootPath,
                DeviceName = serial,
                DeviceOnline = s => FastbootCli.ListDevices(fastbootPath).Any(d => d.Serial == s),
            };
            if (usbBySerial.TryGetValue(serial, out UsbDeviceIdentity identity))
            {
                flasher.UsbVendorId = identity.VendorId;
                flasher.UsbProductId = identity.ProductId;
            }
            try
            {
                flasher.Flash(scriptPath);
                return (serial, true, "flash done");
            }
            catch (Exception ex)
            {
                return (serial, false, ex.Message);
            }
        })).ToArray();

        Task.WaitAll(tasks);

        AnsiConsole.MarkupLine("[bold]" + EdlTargets.Escape(L._("Cli.ResultsHeader")) + "[/]");
        int failures = 0;
        foreach (var (serial, success, message) in tasks.Select(t => t.Result))
        {
            if (!success)
            {
                failures++;
            }
            string color = success ? "green" : "red";
            string verdict = success ? L._("Cli.Success") : L._("Cli.Error");
            AnsiConsole.MarkupLine($"  [{color}]{EdlTargets.Escape(serial)}: {EdlTargets.Escape(verdict)}[/] [grey]{EdlTargets.Escape(message)}[/]");
            string logPath = SessionLog.Complete(serial, success, message);
            if (logPath != null)
            {
                AnsiConsole.MarkupLine($"  [grey]{EdlTargets.Escape(L._("Cli.LogWritten"))}{EdlTargets.Escape(logPath)}[/]");
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static (FlashScript.Variant, string) ParseScriptChoice(string choice)
    {
        return choice switch
        {
            "all" => (FlashScript.Variant.CleanAll, null),
            "except-storage" or "save-user-data" => (FlashScript.Variant.ExceptStorage, null),
            "lock" => (FlashScript.Variant.Lock, null),
            "auto" or null or "" => (FlashScript.Variant.Auto, null),
            _ => (FlashScript.Variant.Auto, choice),
        };
    }

    private static bool Confirm(bool yes, List<string> targets, string scriptPath)
    {
        if (yes)
        {
            return true;
        }
        AnsiConsole.MarkupLine("[bold red]" + EdlTargets.Escape(L._("Cli.EducationalWarning")) + "[/]");
        AnsiConsole.MarkupLine($"  [yellow]flash-fastboot[/] -> {targets.Count} device(s), script {EdlTargets.Escape(System.IO.Path.GetFileName(scriptPath))}");
        if (Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("[red]" + EdlTargets.Escape(L._("Cli.ConfirmRequired")) + "[/]");
            return false;
        }
        Console.Write(L._("Cli.ConfirmPrompt"));
        string answer = Console.ReadLine()?.Trim();
        return string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
