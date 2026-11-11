using MiFlash.Core;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash version</c> — version du portage, runtime et dépôt.
/// </summary>
internal static class VersionCommand
{
    public static Command Build()
    {
        var command = new Command("version", "print version information");
        command.SetAction(_ =>
        {
            Console.WriteLine($"miflash-linux {MiFlashCore.Version} (.NET {Environment.Version}, {Environment.OSVersion})");
            Console.WriteLine("https://github.com/rakmahefa/MiFlash_CSharp_Native_NoAuth — educational use only");
            return 0;
        });
        return command;
    }
}
