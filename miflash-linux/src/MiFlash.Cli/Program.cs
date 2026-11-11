using System;
using MiFlash.Cli.Commands;
using MiFlash.Cli.i18n;
using MiFlash.Core;
using System.CommandLine;

namespace MiFlash.Cli;

/// <summary>
/// Point d'entrée du CLI <c>miflash</c> (M4.1) — System.CommandLine +
/// Spectre.Console. Commandes : devices, flash, backup, restore, provision,
/// reboot, doctor, version. Options globales : --lang fr|en|zh, --verbose.
/// Codes de sortie : 0 succès, 1 échec/opération annulée, 2 usage, 3 aucune
/// cible, 4 diagnostic doctor en échec.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // La langue doit être résolue avant de construire les commandes
        // (libellés) : pré-lecture pragmatique de --lang.
        L.Provider = new LanguageProvider(ResolveLanguage(args));
        try
        {
            // Handler d'exception par défaut désactivé : les erreurs d'usage
            // remontent au catch ci-dessous (message propre, pas de stack trace).
            var configuration = new System.CommandLine.InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
            };
            return BuildRootCommand().Parse(args).Invoke(configuration);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand(L._("Cli.Description"));

        Option<string> lang = new("--lang") { Description = "interface language: fr, en, zh (default: $LANG)", Recursive = true };
        root.Add(lang);

        root.Add(DevicesCommand.Build());
        root.Add(FlashCommand.Build());
        root.Add(BackupCommand.Build());
        root.Add(RestoreCommand.Build());
        root.Add(ProvisionCommand.Build());
        root.Add(RebootCommand.Build());
        root.Add(DoctorCommand.Build());
        root.Add(VersionCommand.Build());

        root.SetAction(_ =>
        {
            // Sans sous-commande : bannière + renvoi vers l'aide (l'usage
            // « miflash » seul de l'atelier n'agit jamais sur un appareil).
            Console.WriteLine("miflash-linux " + MiFlashCore.Version);
            Console.WriteLine(L._("Cli.Description"));
            Console.WriteLine("Run 'miflash --help' for usage.");
            return 0;
        });
        return root;
    }

    /// <summary>Pré-lecture de --lang (avant l'analyse complète) : fr|en|zh.</summary>
    private static string ResolveLanguage(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            string value = null;
            if (args[i] == "--lang")
            {
                value = args[i + 1];
            }
            else if (args[i].StartsWith("--lang=", StringComparison.Ordinal))
            {
                value = args[i].Substring("--lang=".Length);
            }
            if (value != null)
            {
                switch (value.ToLowerInvariant())
                {
                    case "fr": case "fre": return LanguageType.fre;
                    case "en": case "eng": return LanguageType.eng;
                    case "zh": case "chn": case "chn_s": return LanguageType.chn_s;
                    case "cht": case "chn_t": return LanguageType.chn_t;
                }
            }
        }
        return LanguageProvider.Detect();
    }
}
