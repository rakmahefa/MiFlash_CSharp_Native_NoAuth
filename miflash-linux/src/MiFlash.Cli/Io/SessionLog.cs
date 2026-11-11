using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MiFlash.Cli.Io;

/// <summary>
/// Journalisation de session par appareil (M4.5) — port de
/// <c>XiaoMiFlash.code.Utility.Log</c> + <c>moveLog</c> vers les chemins XDG :
/// pendant l'opération, chaque appareil écrit dans
/// <c>~/.local/state/miflash/log/&lt;device&gt;@&lt;yyyyMMdd-HHmmss&gt;.txt</c> ;
/// à la fin le fichier est déplacé vers
/// <c>log/&lt;yyyyMd&gt;/pass|fail/</c> (comme l'original) et une ligne de
/// résultat est ajoutée au journal global <c>log/result.log</c>.
/// Branché sur <see cref="MiFlash.Core.Runtime.Log"/> via
/// <c>Log.Sink = SessionLog.Write</c>.
/// </summary>
public static class SessionLog
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new();

    private sealed class Entry
    {
        public string FilePath;
        public StreamWriter Writer;
        public DateTime Started;
    }

    /// <summary>Répertoire des logs (substituable pour les tests).</summary>
    public static string LogDirectory { get; set; } = MiFlash.Core.Io.AppPaths.LogDir;

    /// <summary>Sink <see cref="MiFlash.Core.Runtime.Log"/> : (deviceName, message).</summary>
    public static void Write(string deviceName, string msg)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            deviceName = "miflash";
        }
        lock (Gate)
        {
            Entry entry = OpenEntry(deviceName);
            entry.Writer.WriteLine("{0:HH:mm:ss.fff} {1}", DateTime.Now, msg);
            entry.Writer.Flush();
        }
    }

    /// <summary>Ouvre (paresseusement) le fichier de session d'un appareil.</summary>
    private static Entry OpenEntry(string deviceName)
    {
        if (Entries.TryGetValue(deviceName, out Entry existing) && existing.Writer != null)
        {
            return existing;
        }
        Directory.CreateDirectory(LogDirectory);
        var entry = new Entry
        {
            Started = DateTime.Now,
            FilePath = Path.Combine(LogDirectory, Sanitize(deviceName) + "@" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt"),
        };
        entry.Writer = new StreamWriter(entry.FilePath, append: false, new UTF8Encoding(false)) { AutoFlush = false };
        Entries[deviceName] = entry;
        return entry;
    }

    /// <summary>
    /// Termine la session d'un appareil : ferme le fichier, le déplace vers
    /// <c>&lt;yyyyMd&gt;/pass|fail/</c> (port de moveLog) et consigne le
    /// résultat dans le journal global <c>result.log</c>.
    /// </summary>
    /// <returns>Chemin final du fichier déplacé (null si aucune session ouverte).</returns>
    public static string Complete(string deviceName, bool success, string detail = null)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return null;
        }
        lock (Gate)
        {
            if (!Entries.TryGetValue(deviceName, out Entry entry) || entry.Writer == null)
            {
                return null;
            }
            entry.Writer.WriteLine("{0:HH:mm:ss.fff} {1}", DateTime.Now, (success ? "success" : "error") + (detail is null ? "" : " — " + detail));
            entry.Writer.Dispose();
            entry.Writer = null;
            Entries.Remove(deviceName);

            // moveLog : log/<yyyyMd>/(pass|fail)/<fichier> — fidèle à l'original.
            string dayDir = Path.Combine(LogDirectory, entry.Started.ToString("yyyyMd", CultureInfo.InvariantCulture));
            string resultDir = Path.Combine(dayDir, success ? "pass" : "fail");
            Directory.CreateDirectory(resultDir);
            string finalPath = Path.Combine(resultDir, Path.GetFileName(entry.FilePath));
            try
            {
                File.Move(entry.FilePath, finalPath, overwrite: true);
            }
            catch (IOException)
            {
                finalPath = entry.FilePath; // cible occupée : garder le fichier actif
            }

            // Journal résultat global (ajout du plan M4.5).
            string resultLog = Path.Combine(LogDirectory, "result.log");
            File.AppendAllText(resultLog, string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss} | {1} | {2} | {3}{4}",
                DateTime.Now, success ? "PASS" : "FAIL", deviceName,
                detail ?? string.Empty, System.Environment.NewLine));
            return finalPath;
        }
    }

    /// <summary>Nettoie un nom d'appareil pour un nom de fichier (bus.port, ttyUSB0…).</summary>
    public static string Sanitize(string deviceName)
    {
        var sb = new StringBuilder(deviceName.Length);
        foreach (char c in deviceName)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '@' ? c : '_');
        }
        return sb.Length == 0 ? "device" : sb.ToString();
    }
}
