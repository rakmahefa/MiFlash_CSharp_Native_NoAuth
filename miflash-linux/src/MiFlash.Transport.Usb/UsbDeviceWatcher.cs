using System;
using System.Collections.Generic;
using System.Threading;

namespace MiFlash.Transport.Usb;

/// <summary>
/// Détection à chaud insert/remove (M2.4) — remplace <c>ManagementEventWatcher</c>
/// (WMI). v1 : polling + diff sur l'identifiant (bus, port) ; le hotplug natif
/// libusb est envisagé au durcissement M6.
/// </summary>
public sealed class UsbDeviceWatcher : IDisposable
{
    private readonly UsbDeviceEnumerator _enumerator;
    private readonly object _lock = new object();
    private Timer _timer;
    private List<UsbDeviceIdentity> _lastSnapshot = new List<UsbDeviceIdentity>();

    /// <summary>Déclenché à l'insertion d'un appareil EDL/fastboot.</summary>
    public event Action<UsbDeviceIdentity> DeviceArrived;

    /// <summary>Déclenché au retrait d'un appareil EDL/fastboot.</summary>
    public event Action<UsbDeviceIdentity> DeviceLeft;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public UsbDeviceWatcher(UsbDeviceEnumerator enumerator = null)
    {
        _enumerator = enumerator ?? new UsbDeviceEnumerator();
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_timer != null)
            {
                return;
            }
            _lastSnapshot = _enumerator.ListDevices();
            _timer = new Timer(_ => Poll(), null, Interval, Interval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Poll()
    {
        List<UsbDeviceIdentity> current;
        try
        {
            current = _enumerator.ListDevices();
        }
        catch (Exception)
        {
            return; // libusb momentanément indisponible : ignorer ce cycle
        }
        List<UsbDeviceIdentity> previous;
        lock (_lock)
        {
            previous = _lastSnapshot;
            _lastSnapshot = current;
        }
        var oldKeys = new HashSet<string>(System.Linq.Enumerable.Select(previous, d => d.Location + "|" + d.SerialNumber));
        var newKeys = new HashSet<string>(System.Linq.Enumerable.Select(current, d => d.Location + "|" + d.SerialNumber));
        foreach (var device in current)
        {
            if (!oldKeys.Contains(device.Location + "|" + device.SerialNumber))
            {
                DeviceArrived?.Invoke(device);
            }
        }
        foreach (var device in previous)
        {
            if (!newKeys.Contains(device.Location + "|" + device.SerialNumber))
            {
                DeviceLeft?.Invoke(device);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
    }
}
