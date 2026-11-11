using MiFlash.Edl;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash reboot [dir] [--edl]</c> — redémarre la cible via partial_reset
/// (firehose) ; <c>--edl</c> la laisse en mode 9008 (reset_to_edl). Le
/// répertoire d'images (programmers) est requis pour ouvrir la session
/// firehose — défaut : répertoire courant.
/// </summary>
internal static class RebootCommand
{
    public static Command Build()
    {
        Argument<DirectoryInfo> dir = new("dir")
        {
            Description = "image directory holding the programmers (default: current)",
            DefaultValueFactory = _ => new DirectoryInfo("."),
        };
        Option<bool> edl = new("--edl") { Description = "reboot back into EDL 9008 mode (reset_to_edl)" };
        Option<List<string>> device = EdlCli.DeviceOption();
        Option<string> serial = EdlCli.SerialOption();
        Option<string> programmer = EdlCli.ProgrammerOption();
        Option<bool> readDump = EdlCli.ReadDumpOption();
        Option<bool> writeDump = EdlCli.WriteDumpOption();
        Option<bool> quiet = EdlCli.QuietOption();

        var command = new Command("reboot", "reboot device (partial_reset, or --edl for EDL mode)");
        command.Add(dir);
        command.Add(edl);
        command.Add(device);
        command.Add(serial);
        command.Add(programmer);
        command.Add(readDump);
        command.Add(writeDump);
        command.Add(quiet);
        command.SetAction(parseResult =>
        {
            DirectoryInfo directory = parseResult.GetValue(dir);
            string path = directory?.FullName ?? ".";
            if (!System.IO.Directory.Exists(path))
            {
                throw new System.ArgumentException("directory not found: " + path);
            }
            var options = new EdlRun.Options
            {
                Directory = path,
                Device = parseResult.GetValue(device) ?? new List<string>(),
                Serial = parseResult.GetValue(serial),
                Programmer = EdlCli.ParseProgrammer(parseResult.GetValue(programmer)),
                ReadDump = parseResult.GetValue(readDump),
                WriteDump = parseResult.GetValue(writeDump),
                Quiet = parseResult.GetValue(quiet),
            };
            return EdlRun.Run(options,
                parseResult.GetValue(edl) ? EdlOperation.RebootToEdl : EdlOperation.Reboot,
                destructive: false);
        });
        return command;
    }
}
