using MiFlash.Edl;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash backup &lt;dir&gt;</c> — sauvegarde des partitions listées par
/// rawprogram_backup.xml vers le répertoire de session (lecture seule, aucune
/// écriture sur la cible). Le répertoire de sauvegarde est affiché en résultat.
/// </summary>
internal static class BackupCommand
{
    public static Command Build()
    {
        Argument<DirectoryInfo> dir = EdlCli.DirectoryArg("image directory containing rawprogram_backup.xml (and programmers)");
        Option<List<string>> device = EdlCli.DeviceOption();
        Option<string> serial = EdlCli.SerialOption();
        Option<string> programmer = EdlCli.ProgrammerOption();
        Option<string> backupDir = EdlCli.BackupDirOption();
        Option<bool> readDump = EdlCli.ReadDumpOption();
        Option<bool> writeDump = EdlCli.WriteDumpOption();
        Option<bool> quiet = EdlCli.QuietOption();
        Option<bool> yes = EdlCli.YesOption();

        var command = new Command("backup", "backup partitions listed by rawprogram_backup.xml (read-only)");
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
            return EdlRun.Run(options, EdlOperation.Backup, destructive: false);
        });
        return command;
    }
}
