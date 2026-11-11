using MiFlash.Edl;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash provision &lt;dir&gt;</c> — provisionnement UFS depuis
/// provision*.xml (boucle provision de XiaomiFlashNew isolée). Opération
/// destructive (reconfigure le stockage de la cible).
/// </summary>
internal static class ProvisionCommand
{
    public static Command Build()
    {
        Argument<DirectoryInfo> dir = EdlCli.DirectoryArg("image directory containing provision*.xml (and programmers)");
        Option<List<string>> device = EdlCli.DeviceOption();
        Option<string> serial = EdlCli.SerialOption();
        Option<string> programmer = EdlCli.ProgrammerOption();
        Option<string> backupDir = EdlCli.BackupDirOption();
        Option<bool> readDump = EdlCli.ReadDumpOption();
        Option<bool> writeDump = EdlCli.WriteDumpOption();
        Option<bool> quiet = EdlCli.QuietOption();
        Option<bool> yes = EdlCli.YesOption();

        var command = new Command("provision", "provision UFS storage from provision*.xml");
        command.Add(dir);
        command.Add(device);
        command.Add(serial);
        command.Add(programmer);
        command.Add(backupDir);
        command.Add(readDump);
        command.Add(writeDump);
        command.Add(quiet);
        command.Add(yes);
        command.SetAction(parseResult =>
        {
            EdlRun.Options options = EdlCli.ReadCommon(parseResult, dir, device, serial, programmer, backupDir, readDump, writeDump, quiet, yes);
            return EdlRun.Run(options, EdlOperation.Provision, destructive: true);
        });
        return command;
    }
}
