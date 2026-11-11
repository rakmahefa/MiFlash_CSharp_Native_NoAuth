using System;
using System.IO;

namespace MiFlash.Core.Io;

/// <summary>
/// Chemins applicatifs conformes XDG (M1.6) — remplace les chemins Windows
/// <c>AppDomain…ApplicationBase + "Source\\ThirdParty\\…"</c> et <c>C:\fastbootlog</c>.
/// Les propriétés sont évaluées à chaque accès afin de rester testables via
/// les variables d'environnement XDG.
/// </summary>
public static class AppPaths
{
    /// <summary>Racine des données (images, config par appareil) : <c>$XDG_DATA_HOME/miflash</c> (défaut <c>~/.local/share/miflash</c>).</summary>
    public static string DataRoot => XdgDir("XDG_DATA_HOME", ".local/share") + "/miflash";

    /// <summary>Racine des états (logs, historique) : <c>$XDG_STATE_HOME/miflash</c> (défaut <c>~/.local/state/miflash</c>).</summary>
    public static string StateRoot => XdgDir("XDG_STATE_HOME", ".local/state") + "/miflash";

    /// <summary>Répertoire des logs par appareil : <c>~/.local/state/miflash/log</c>.</summary>
    public static string LogDir => Path.Combine(StateRoot, "log");

    /// <summary>Fichier de configuration utilisateur : <c>$XDG_CONFIG_HOME/miflash/config.json</c> (défaut <c>~/.config/miflash/config.json</c>).</summary>
    public static string ConfigFile => Path.Combine(XdgDir("XDG_CONFIG_HOME", ".config"), "miflash", "config.json");

    private static string XdgDir(string envVar, string defaultRelative)
    {
        string value = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(value) && Path.IsPathRooted(value))
        {
            return value;
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, defaultRelative);
    }

    /// <summary>Crée les répertoires standard au premier usage.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
    }
}
