using MiFlash.Edl;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash restore &lt;dir&gt; [--from &lt;backup_session&gt;]</c> —
/// restaure les images listées par rawprogram_restore.xml depuis le répertoire
/// d'une sauvegarde (<c>--from</c>) ou de la session courante. Opération
/// destructive (écrit dans les partitions de la cible).
/// </summary>
internal static class RestoreCommand
{
    public static Command Build()
    {
        Argument<DirectoryInfo> dir = EdlCli.DirectoryArg("image directory containing rawprogram_restore.xml (and programmers)");
        Option<string> from = new("--from") { Description = "backup session directory to restore from (as printed by 'miflash backup')" };
        Option<List<string>> device = EdlCli.DeviceOption();
        Option<string> serial = EdlCli.SerialOption();
        Option<string> programmer = EdlCli.ProgrammerOption();
        Option<string> backupDir = EdlCli.BackupDirOption();
        Option<bool> readDump = EdlCli.ReadDumpOption();
        Option<bool> writeDump = EdlCli.WriteDumpOption();
        Option<bool> quiet = EdlCli.QuietOption();
        Option<bool> yes = EdlCli.YesOption();

        var command = new Command("restore", "restore partitions listed by rawprogram_restore.xml");
        command.Add(dir);
        command.Add(from);
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
            string restoreFrom = parseResult.GetValue(from);
            if (!string.IsNullOrEmpty(restoreFrom) && !System.IO.Directory.Exists(restoreFrom))
            {
                throw new System.ArgumentException("backup directory not found: " + restoreFrom);
            }
            return EdlRun.Run(options, EdlOperation.Restore, destructive: true, restoreFrom: restoreFrom);
        });
        return command;
    }
}
