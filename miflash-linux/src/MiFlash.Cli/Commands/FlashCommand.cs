using MiFlash.Edl;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash flash &lt;dir&gt;</c> (M4.3, RF-3→RF-11) — flash complet du
/// répertoire d'images (programmers, rawprogram*.xml, patch*.xml, provision,
/// backup/restore) : port de l'opération « flash » de l'atelier Windows.
/// </summary>
internal static class FlashCommand
{
    public static Command Build()
    {
        Argument<DirectoryInfo> dir = EdlCli.DirectoryArg("image directory (programmers + rawprogram/patch XML)");
        Option<List<string>> device = EdlCli.DeviceOption();
        Option<string> serial = EdlCli.SerialOption();
        Option<string> programmer = EdlCli.ProgrammerOption();
        Option<string> backupDir = EdlCli.BackupDirOption();
        Option<bool> readDump = EdlCli.ReadDumpOption();
        Option<bool> writeDump = EdlCli.WriteDumpOption();
        Option<bool> quiet = EdlCli.QuietOption();
        Option<bool> yes = EdlCli.YesOption();

        var command = new Command("flash", "flash an image directory to EDL device(s)");
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
            return EdlRun.Run(options, EdlOperation.Flash, destructive: true);
        });
        return command;
    }
}
