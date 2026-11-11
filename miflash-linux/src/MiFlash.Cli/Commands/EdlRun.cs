using System;
using System.Collections.Generic;
using System.Linq;
using MiFlash.Cli.Io;
using MiFlash.Cli.i18n;
using MiFlash.Cli.Ui;
using MiFlash.Edl;
using MiFlash.Core.Runtime;
using Spectre.Console;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// Pipeline commun des opérations EDL (M4.3/M4.4/M4.5) : résolution des
/// cibles → confirmation éducative (sauf <c>--yes</c>) → exécution via
/// <see cref="MultiDeviceFlasher"/> (parallèle, échec isolé par appareil) →
/// journalisation par appareil (<see cref="SessionLog"/> : fichiers
/// pass|fail + result.log) et code de sortie (0 succès, 1 échec, 3 aucune
/// cible).
/// </summary>
internal static class EdlRun
{
    /// <summary>Options communes aux commandes EDL.</summary>
    public sealed class Options
    {
        public string Directory;
        public List<string> Device;
        public string Serial;
        public int Programmer;          // 0 auto, 1 lite, 2 ddr4, 3 ddr5
        public string BackupDir;
        public bool ReadDump;
        public bool WriteDump;
        public bool Quiet;
        public bool Yes;
    }

    /// <summary>Exécute une opération EDL sur les cibles résolues.</summary>
    public static int Run(Options options, EdlOperation operation, bool destructive, string restoreFrom = null)
    {
        List<(string Name, Func<Core.Transport.ITransport> Factory)> targets;
        try
        {
            targets = EdlTargets.Resolve(options.Device, options.Serial);
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine("[red]" + EdlTargets.Escape(ex.Message) + "[/]");
            return 3;
        }
        if (targets.Count == 0)
        {
            return EdlTargets.ReportNoDevice();
        }

        if (destructive && !Confirm(options.Yes, targets, operation))
        {
            AnsiConsole.MarkupLine("[yellow]" + EdlTargets.Escape(L._("Cli.Aborted")) + "[/]");
            return 1;
        }

        var jobs = new List<EdlJob>();
        foreach ((string name, var factory) in targets)
        {
            string capturedName = name;
            var capturedFactory = factory;
            jobs.Add(new EdlJob
            {
                DeviceName = capturedName,
                SwPath = options.Directory,
                Operation = operation,
                RestoreFrom = restoreFrom,
                TransportFactory = capturedFactory,
                Configure = flasher =>
                {
                    flasher.programmerName = options.Programmer;
                    flasher.verbose = !options.Quiet;
                    if (!string.IsNullOrEmpty(options.BackupDir))
                    {
                        flasher.RestoreRoot = options.BackupDir;
                    }
                    flasher.comm.isReadDump = options.ReadDump;
                    flasher.comm.isWriteDump = options.WriteDump;
                },
            });
        }

        Log.Sink = SessionLog.Write;
        using var reporter = new ConsoleReporter();
        FlashStatus.Reporter = reporter;

        var flasher = new MultiDeviceFlasher();
        IReadOnlyList<EdlJobResult> results = flasher.Run(jobs);

        AnsiConsole.MarkupLine("[bold]" + EdlTargets.Escape(L._("Cli.ResultsHeader")) + "[/]");
        int failures = 0;
        foreach (EdlJobResult result in results)
        {
            if (!result.Success)
            {
                failures++;
            }
            string color = result.Success ? "green" : "red";
            string verdict = result.Success ? L._("Cli.Success") : L._("Cli.Error");
            string message = result.Success ? result.Message : (result.Message ?? result.Error?.Message ?? "");
            AnsiConsole.MarkupLine($"  [{color}]{EdlTargets.Escape(result.DeviceName)}: {EdlTargets.Escape(verdict)}[/] [grey]{EdlTargets.Escape(message)}[/]");
            string logPath = SessionLog.Complete(result.DeviceName, result.Success, message);
            if (logPath != null)
            {
                AnsiConsole.MarkupLine($"  [grey]{EdlTargets.Escape(L._("Cli.LogWritten"))}{EdlTargets.Escape(logPath)}[/]");
            }
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Confirmation avant erase/program (M4.4) : avertissement éducatif,
    /// cibles et répertoire listés, saisie « yes » — ou <c>--yes</c> ;
    /// stdin non terminal sans <c>--yes</c> → refus (scripts explicites).
    /// </summary>
    private static bool Confirm(bool yes, List<(string Name, Func<Core.Transport.ITransport> Factory)> targets, EdlOperation operation)
    {
        if (yes)
        {
            return true;
        }
        AnsiConsole.MarkupLine("[bold red]" + EdlTargets.Escape(L._("Cli.EducationalWarning")) + "[/]");
        AnsiConsole.MarkupLine($"  [yellow]{operation}[/] -> {targets.Count} device(s)");
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
