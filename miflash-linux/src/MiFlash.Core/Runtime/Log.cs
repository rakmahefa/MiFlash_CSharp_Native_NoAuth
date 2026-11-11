using System;
using System.Text;

namespace MiFlash.Core.Runtime;

/// <summary>
/// Journalisation minimale du cœur protocole (port de l'API Log.cs utilisée
/// par Comm/FileTransfer). Le branchement vers les fichiers XDG
/// (<c>~/.local/state/miflash/log/</c>) se fait au jalon M4 via <see cref="Sink"/>;
/// par défaut, sortie stderr.
/// </summary>
public static class Log
{
    /// <summary>Sink substituable : (deviceName, message).</summary>
    public static Action<string, string> Sink { get; set; } = static (_, msg) => Console.Error.WriteLine(msg);

    public static void w(string deviceName, string msg)
    {
        Sink(deviceName, msg);
    }

    public static void w(string msg)
    {
        Sink(string.Empty, msg);
    }

    /// <summary>Message + exception ; <paramref name="throwEx"/> conservé pour compatibilité appelants.</summary>
    public static void w(string deviceName, string msg, bool throwEx)
    {
        Sink(deviceName, msg);
        if (throwEx)
        {
            throw new Exception(msg);
        }
    }

    public static void w(string deviceName, Exception ex, bool stopFlash)
    {
        Sink(deviceName, (stopFlash ? "[STOP] " : string.Empty) + ex.Message);
    }

    /// <summary>Dump hexadécimal + ASCII d'un tampon (équivalent debugString).</summary>
    public static void debugString(string deviceName, byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
        {
            return;
        }
        var sb = new StringBuilder(buffer.Length * 2);
        for (int i = 0; i < buffer.Length; i++)
        {
            sb.Append(buffer[i].ToString("X2"));
            if (i % 32 == 31)
            {
                sb.AppendLine();
            }
            else
            {
                sb.Append(' ');
            }
        }
        Sink(deviceName, sb.ToString());
    }
}
