using MiFlash.Core;

namespace MiFlash.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Squelette M0.3 — les commandes réelles (devices|flash|backup|restore|
        // provision|reboot|doctor|version) arrivent au jalon M4 (todo.md M4.1).
        if (args is ["--version"] or ["version"])
        {
            Console.WriteLine($"miflash-linux {MiFlashCore.Version} (.NET {Environment.Version}, {Environment.OSVersion})");
            return 0;
        }

        Console.WriteLine("miflash-linux — squelette M0.3 du portage natif Linux de MiFlash");
        Console.WriteLine("Commandes : voir miflash-linux/README.md (jalon M4 pour la CLI complète)");
        return 0;
    }
}
