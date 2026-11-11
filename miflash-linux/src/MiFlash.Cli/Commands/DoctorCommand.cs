using System.Linq;
using MiFlash.Cli.Doctor;
using MiFlash.Cli.i18n;
using Spectre.Console;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash doctor</c> (M4.7) — vérifications d'environnement : runtime .NET,
/// libusb-1.0, règles udev, binaire fastboot, répertoires XDG. Sortie 0 si
/// aucun échec (les avertissements n'échouent pas), 4 sinon.
/// </summary>
internal static class DoctorCommand
{
    public static Command Build()
    {
        var command = new Command("doctor", "check environment (libusb, udev, permissions, fastboot, .NET)");
        command.SetAction(_ => Run());
        return command;
    }

    private static int Run()
    {
        AnsiConsole.MarkupLine("[bold]" + EdlTargets.Escape(L._("Cli.DoctorTitle")) + "[/]");
        var checks = DoctorService.Run();
        int failures = 0;
        foreach (DoctorService.Check check in checks)
        {
            if (check.Status == DoctorService.CheckStatus.Fail)
            {
                failures++;
            }
            string badge = check.Status switch
            {
                DoctorService.CheckStatus.Ok => "[green]" + L._("Cli.Doctor.Ok") + "[/]",
                DoctorService.CheckStatus.Warn => "[yellow]" + L._("Cli.Doctor.Warn") + "[/]",
                _ => "[red]" + L._("Cli.Doctor.Fail") + "[/]",
            };
            AnsiConsole.MarkupLine($"  {badge,-14} [bold]{EdlTargets.Escape(check.Name)}[/]: {EdlTargets.Escape(check.Detail)}");
        }
        return failures == 0 ? 0 : 4;
    }
}
