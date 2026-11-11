using System;
using System.IO;
using System.Linq;
using MiFlash.Core.Io;

namespace MiFlash.Fastboot;

/// <summary>
/// Sélection du script de flash (M5.3) — port de <c>FlashType</c> : clean all
/// (<c>flash_all.bat</c>), except-storage (<c>flash_all_except.*\.bat</c>),
/// lock (<c>flash_all_lock.bat</c>). Sur Linux les scripts .sh des ROM sont
/// acceptés en équivalent (même parsing par <see cref="ScriptEngine"/>).
/// </summary>
public static class FlashScript
{
    public const string CleanAll = "flash_all.bat";

    public const string SaveUserDataPattern = "flash_all_except.*\\.bat";

    public const string CleanAllAndLock = "flash_all_lock.bat";

    /// <summary>Choix utilisateur du CLI : variante ou nom de fichier exact.</summary>
    public enum Variant { Auto, CleanAll, ExceptStorage, Lock }

    /// <summary>
    /// Résout le script à exécuter dans le répertoire ROM : variante ou nom
    /// exact (.bat prioritaire, sinon .sh équivalent). Retourne null si
    /// introuvable.
    /// </summary>
    public static string Resolve(string romDirectory, Variant variant, string explicitScript = null)
    {
        if (!string.IsNullOrEmpty(explicitScript))
        {
            return FirstOrDefault(romDirectory, EscapeExact(explicitScript));
        }
        return variant switch
        {
            Variant.CleanAll => FirstOrDefault(romDirectory, CleanAll),
            Variant.Lock => FirstOrDefault(romDirectory, CleanAllAndLock),
            Variant.ExceptStorage => FirstOrDefault(romDirectory, SaveUserDataPattern),
            Variant.Auto => FirstOrDefault(romDirectory, CleanAll)
                            ?? FirstOrDefault(romDirectory, SaveUserDataPattern)
                            ?? FirstOrDefault(romDirectory, CleanAllAndLock),
            _ => null,
        };
    }

    /// <summary>Cherche le fichier (motif exact ou regex) puis son équivalent
    /// .sh (remplacement d'extension dans le motif).</summary>
    private static string FirstOrDefault(string romDirectory, string fileOrPattern)
    {
        string bat = FileSearcher.SearchFiles(romDirectory, fileOrPattern).FirstOrDefault();
        if (bat != null)
        {
            return bat;
        }
        string shPattern = SwapShExtension(fileOrPattern);
        return FileSearcher.SearchFiles(romDirectory, shPattern).FirstOrDefault();
    }

    private static string SwapShExtension(string fileOrPattern)
    {
        if (fileOrPattern.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return fileOrPattern.Substring(0, fileOrPattern.Length - 4) + ".sh";
        }
        return fileOrPattern;
    }

    /// <summary>Échappe un nom de fichier exact en motif regex pleine correspondance.</summary>
    private static string EscapeExact(string fileName)
    {
        return "^" + System.Text.RegularExpressions.Regex.Escape(Path.GetFileName(fileName)) + "$";
    }
}
