using System;
using System.Collections.Generic;
using Spectre.Console;

namespace MiFlash.Cli.Ui;

/// <summary>
/// Rendu console des statuts de flash (M4.3) — implémente
/// <see cref="MiFlash.Core.Runtime.IFlashStatusReporter"/> avec Spectre.Console :
/// une ligne par changement d'état, préfixe appareil, couleur selon le résultat
/// (flashing=jaune, provisioning=bleu, success=vert, error=rouge). Les mises à
/// jour répétées sans changement d'état d'un même appareil sont ignorées (le
/// cœur protocole est très bavard) ; chaque appareil garde son propre anti-spam.
/// </summary>
public sealed class ConsoleReporter : MiFlash.Core.Runtime.IFlashStatusReporter, IDisposable
{
    private readonly Dictionary<string, string> _lastKeyPerDevice = new();

    public void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone)
    {
        if (string.IsNullOrEmpty(status) && string.IsNullOrEmpty(result))
        {
            return;
        }
        string key = (status ?? "") + "|" + (result ?? "");
        if (!isDone && _lastKeyPerDevice.TryGetValue(deviceName ?? "", out string last) && last == key)
        {
            return;
        }
        _lastKeyPerDevice[deviceName ?? ""] = key;

        string color = result switch
        {
            "success" => "green",
            "error" => "red",
            "provisioning" => "deepskyblue1",
            _ => "yellow",
        };
        string message = status;
        if (string.IsNullOrEmpty(message))
        {
            message = result;
        }
        if (progress.HasValue)
        {
            int filled = Math.Clamp((int)Math.Round(progress.Value * 10), 0, 10);
            message += " [" + new string('#', filled) + new string('.', 10 - filled) + "]";
        }
        string line = $"[grey]{DateTime.Now:HH:mm:ss}[/] [[{Escape(deviceName)}]] [{color}]{Escape(message)}[/]";
        if (isDone)
        {
            line += $" [{color}]({Escape(result ?? "")})[/]";
        }
        AnsiConsole.MarkupLine(line);
    }

    private static string Escape(string text)
    {
        return (text ?? string.Empty).Replace("[", "[[").Replace("]", "]]");
    }

    public void Dispose()
    {
    }
}
