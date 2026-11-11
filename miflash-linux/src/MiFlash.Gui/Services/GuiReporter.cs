using System;
using MiFlash.Core.Runtime;

namespace MiFlash.Gui.Services;

/// <summary>
/// <see cref="IFlashStatusReporter"/> du GUI (M6.3) : relève les statuts du
/// moteur (threads de flash) et les relaie sous forme d'événement — le
/// ViewModel marshale ensuite vers le thread UI. Équivalent GUI de
/// <c>ConsoleReporter</c> (CLI M4.3).
/// </summary>
public sealed class GuiReporter : IFlashStatusReporter, IDisposable
{
    /// <summary>Statut relevé (thread quelconque — marshaler côté abonné).</summary>
    public sealed record Status(string DeviceName, float? Progress, string StatusText, string Result, bool IsDone);

    private readonly object _gate = new();

    /// <summary>Abonnés (appelés en séquence sous verrou, code court uniquement).</summary>
    private event Action<Status> _handlers;

    public event Action<Status> Received
    {
        add { lock (_gate) { _handlers += value; } }
        remove { lock (_gate) { _handlers -= value; } }
    }

    public void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone)
    {
        Action<Status> handlers;
        lock (_gate)
        {
            handlers = _handlers;
        }
        handlers?.Invoke(new Status(deviceName, progress, status, result, isDone));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _handlers = null;
        }
    }
}
