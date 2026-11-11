using System;
using System.Collections.Generic;
using System.Linq;
using MiFlash.Cli.i18n;
using MiFlash.Transport.Serial;
using MiFlash.Transport.Usb;
using Spectre.Console;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// Cibles EDL d'une commande (M4.3) : résout <c>--serial &lt;port&gt;</c>
/// (fallback qcserial) ou les appareils 9008 USB, filtrés par
/// <c>--device bus.port|serial</c> (répétable ; défaut : tous les 9008).
/// Remplace la sélection par index COM de l'atelier Windows.
/// </summary>
internal static class EdlTargets
{
    /// <summary>Résout les transports EDL : un couple (nom, fabrique) par cible.</summary>
    public static List<(string Name, Func<MiFlash.Core.Transport.ITransport> Factory)> Resolve(
        IEnumerable<string> deviceSelectors,
        string serialPort)
    {
        var targets = new List<(string, Func<MiFlash.Core.Transport.ITransport>)>();
        if (!string.IsNullOrEmpty(serialPort))
        {
            targets.Add((serialPort, () => new SerialTransport(serialPort)));
            return targets;
        }

        using var enumerator = new UsbDeviceEnumerator();
        List<UsbDeviceIdentity> devices = enumerator.ListDevices()
            .Where(d => d.Kind == UsbDeviceKind.Edl9008)
            .ToList();

        List<string> selectors = deviceSelectors?.Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();
        if (selectors.Count > 0)
        {
            foreach (string selector in selectors)
            {
                UsbDeviceIdentity match = devices.FirstOrDefault(d => Matches(d, selector));
                if (match == null)
                {
                    throw new InvalidOperationException(L._("Cli.DeviceNotFound") + selector);
                }
                targets.Add((DeviceLabel(match), () => OpenByIdentity(match)));
            }
            return targets;
        }

        foreach (UsbDeviceIdentity device in devices)
        {
            UsbDeviceIdentity captured = device;
            targets.Add((DeviceLabel(captured), () => OpenByIdentity(captured)));
        }
        return targets;
    }

    /// <summary>Ouvre un 9008 par identité (bus, port) — fabrique par travail,
    /// un contexte libusb frais à chaque ouverture (MultiDeviceFlasher).</summary>
    private static MiFlash.Core.Transport.ITransport OpenByIdentity(UsbDeviceIdentity identity)
    {
        var enumerator = new UsbDeviceEnumerator();
        try
        {
            return enumerator.OpenEdlDevice(identity);
        }
        catch
        {
            enumerator.Dispose();
            throw;
        }
    }

    /// <summary>Étiquette stable : <c>9008@bus.port</c> (+sn si connu).</summary>
    public static string DeviceLabel(UsbDeviceIdentity device)
    {
        return "9008@" + device.Location + (string.IsNullOrEmpty(device.SerialNumber) ? "" : "#" + device.SerialNumber);
    }

    /// <summary>Un sélecteur matche la localisation (1.3), « bus.port » ou le numéro de série.</summary>
    public static bool Matches(UsbDeviceIdentity device, string selector)
    {
        return string.Equals(device.Location, selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(device.BusNumber + "." + device.PortNumber, selector, StringComparison.OrdinalIgnoreCase)
            || (device.SerialNumber.Length > 0 && string.Equals(device.SerialNumber, selector, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Message d'aide quand aucune cible n'est trouvée (exit 3).</summary>
    public static int ReportNoDevice()
    {
        AnsiConsole.MarkupLine("[red]" + Escape(L._("Cli.NoEdlDevice")) + "[/]");
        return 3;
    }

    public static string Escape(string text)
    {
        return (text ?? string.Empty).Replace("[", "[[").Replace("]", "]]");
    }
}
