using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MiFlash.Fastboot;

/// <summary>Une opération extraite d'un script flash_all (M5.3).</summary>
public sealed class FastbootOp
{
    /// <summary>Verbe fastboot : flash, erase, reboot, oem, boot, set_active… (« sleep » pour les attentes).</summary>
    public string Verb;

    /// <summary>Arguments après le verbe (chemins déjà résolus en absolu POSIX).</summary>
    public ArgumentsBuilder Args = new();

    /// <summary>Attente en secondes (op « sleep » des ping -n / sleep n).</summary>
    public int SleepSeconds;

    /// <summary>Ligne d'origine (journalisation).</summary>
    public string SourceLine;

    public override string ToString()
    {
        return SleepSeconds > 0 ? "sleep " + SleepSeconds : "fastboot " + Verb + " " + Args;
    }
}

/// <summary>Arguments accumulés d'une opération (mots + chemins quotés).</summary>
public sealed class ArgumentsBuilder
{
    private readonly List<string> _words = new();

    public void Add(string word)
    {
        if (!string.IsNullOrEmpty(word))
        {
            _words.Add(word);
        }
    }

    public IReadOnlyList<string> Words => _words;

    public override string ToString()
    {
        return string.Join(" ", _words);
    }
}

/// <summary>
/// Moteur de scripts natif (M5.3) — parsing des <c>flash_all*.bat</c> (et de
/// leurs équivalents <c>.sh</c>) en opérations fastboot, exécutées ensuite
/// directement par <see cref="FastbootFlasher"/> sans cmd.exe ni shell :
/// remplace la ligne <c>pushd …&&set PATH=…&&script.bat -s com&&popd</c> de
/// <c>ScriptDevice.fastbootflash</c>. Règles :
/// <list type="bullet">
/// <item><c>%~dp0</c>/<c>set "filepath=…"</c>/<c>filepath=$(…)</c> → répertoire du script ;</item>
/// <item><c>fastboot %* verbe args</c> → opération verbe args (le <c>-s serial</c> est réinjecté à l'exécution) ;</item>
/// <item><c>|| @echo … &amp;&amp; exit /B 1</c> / <c>|| exit 1</c> → échec si code de sortie non nul (géré à l'exécution) ;</item>
/// <item><c>ping -n N 127.0.0.1</c>/<c>sleep N</c> → attente N s ;</item>
/// <item>chemins Windows <c>%filepath%images\x</c> → <c>&lt;dir&gt;/images/x</c> ;</item>
/// <item><c>exit</c> (0 ou /B) → fin du script.</item>
/// </list>
/// </summary>
public static partial class ScriptEngine
{
    /// <summary>Parse le script en opérations ordonnées.</summary>
    public static List<FastbootOp> Parse(string scriptPath)
    {
        var ops = new List<FastbootOp>();
        string scriptDir = Path.GetFullPath(Path.GetDirectoryName(scriptPath));
        string filepath = scriptDir + "/";
        foreach (string rawLine in File.ReadAllLines(scriptPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Commentaires / no-op cmd.
            string lower = line.ToLowerInvariant();
            if (lower.StartsWith("::") || lower.StartsWith("#")
                || lower.StartsWith("rem ") || lower == "rem"
                || lower.StartsWith("@echo") || lower.StartsWith("echo.")
                || lower.StartsWith("title") || lower.StartsWith("prompt")
                || lower.StartsWith("popd") || lower.StartsWith("pushd")
                || lower.StartsWith("set -") || lower.StartsWith("cd ")
                || lower == "cd")
            {
                continue;
            }

            // set "filepath=%~dp0" / set filepath=%~dp0 / filepath=$(cd "$(dirname "$0")"; pwd -P)
            if (TryParseFilePath(line, scriptDir, out string newFilepath))
            {
                filepath = newFilepath;
                continue;
            }

            // exit / exit /B 0 : fin du script.
            if (lower == "exit" || lower.StartsWith("exit ") || lower.StartsWith("exit/b"))
            {
                break;
            }

            // Attentes : ping -n N 127.0.0.1 (>nul) ≈ N-1 s ; sleep N.
            Match sleep = SleepPingRegex().Match(line);
            if (sleep.Success)
            {
                int seconds = Math.Max(0, int.Parse(sleep.Groups[1].Value, CultureInfo.InvariantCulture) - 1);
                if (seconds > 0)
                {
                    ops.Add(new FastbootOp { Verb = "sleep", SleepSeconds = seconds, SourceLine = line });
                }
                continue;
            }
            Match shSleep = ShSleepRegex().Match(line);
            if (shSleep.Success)
            {
                if (double.TryParse(shSleep.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s > 0)
                {
                    ops.Add(new FastbootOp { Verb = "sleep", SleepSeconds = (int)Math.Ceiling(s), SourceLine = line });
                }
                continue;
            }

            // fastboot %* verbe args… (le binaire peut être quoté ou préfixé d'un chemin).
            Match fastbootLine = FastbootLineRegex().Match(line);
            if (fastbootLine.Success)
            {
                string tail = fastbootLine.Groups["args"].Value;
                // Couper la gestion d'erreur en queue : || @echo "…" && exit /B 1, || exit 1…
                tail = ErrorTailRegex().Split(tail)[0].Trim();
                FastbootOp op = ParseFastbootArgs(tail, filepath);
                if (op != null)
                {
                    op.SourceLine = line;
                    ops.Add(op);
                }
                continue;
            }
            // Lignes non reconnues (echo d'information, etc.) : ignorées.
        }
        return ops;
    }

    /// <summary>Découpe « verbe args… » et résout les chemins (%filepath%, $filepath, \ → /).</summary>
    private static FastbootOp ParseFastbootArgs(string tail, string filepath)
    {
        // Neutraliser %* et $* (le -s serial est réinjecté par le flasher).
        tail = tail.Replace("%*", string.Empty).Replace("$*", string.Empty).Replace("$@", string.Empty).Trim();
        if (tail.Length == 0)
        {
            return null;
        }
        var op = new FastbootOp();
        foreach (string token in Tokenize(tail))
        {
            string word = token;
            // Substitution des variables de répertoire (valeur « …/ » finale).
            word = word
                .Replace("%~dp0", filepath, StringComparison.OrdinalIgnoreCase)
                .Replace("%filepath%", filepath, StringComparison.OrdinalIgnoreCase);
            word = ShVarRegex().Replace(word, filepath);
            // Chemins Windows (images\boot.img) → POSIX, doubles slashs repliés.
            word = word.Replace('\\', '/').Replace("//", "/");
            if (op.Verb == null)
            {
                op.Verb = word;
                continue;
            }
            op.Args.Add(word);
        }
        return op.Verb == null ? null : op;
    }

    /// <summary>Découpe en mots, en respectant les guillemets « … ».</summary>
    private static IEnumerable<string> Tokenize(string line)
    {
        var words = new List<string>();
        foreach (Match m in WordRegex().Matches(line))
        {
            string word = m.Value;
            if (word.Length >= 2 && word.StartsWith('"') && word.EndsWith('"'))
            {
                word = word.Substring(1, word.Length - 2);
            }
            words.Add(word);
        }
        return words;
    }

    private static bool TryParseFilePath(string line, string scriptDir, out string filepath)
    {
        filepath = null;
        string resolved = scriptDir.Replace('\\', '/').TrimEnd('/') + "/";
        // set "filepath=%~dp0" | set filepath=%~dp0
        Match m = SetFilepathRegex().Match(line);
        if (m.Success)
        {
            string value = m.Groups[1].Value.Trim('"');
            if (value.Contains("%~dp0"))
            {
                filepath = resolved;
                return true;
            }
        }
        // filepath=$(cd "$(dirname "$0")"; pwd -P)
        if (ShFilepathRegex().IsMatch(line))
        {
            filepath = resolved;
            return true;
        }
        return false;
    }

    [GeneratedRegex(@"^set\s+""?filepath""?\s*=\s*(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SetFilepathRegex();

    [GeneratedRegex(@"^\s*filepath\s*=\$\(\s*cd\s+""?\$\(\s*dirname\s+""?\$0""?\s*\)\s*""?\s*;\s*pwd\s+-P\s*\)")]
    private static partial Regex ShFilepathRegex();

    [GeneratedRegex(@"^ping\s+-n\s+(\d+)\s+127\.0\.0\.1", RegexOptions.IgnoreCase)]
    private static partial Regex SleepPingRegex();

    [GeneratedRegex(@"^sleep\s+([\d.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ShSleepRegex();

    [GeneratedRegex(@"^(?:@)?(?:""[^""]*""|\S*fastboot\S*)\s+(?:%\*|\$\*|\$@)?\s*(?<args>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FastbootLineRegex();

    [GeneratedRegex(@"\|\|")]
    private static partial Regex ErrorTailRegex();

    [GeneratedRegex(@"\$\{?filepath\}?")]
    private static partial Regex ShVarRegex();

    [GeneratedRegex(@"""[^""]*""|\S+")]
    private static partial Regex WordRegex();
}
