namespace MiFlash.Transport.Usb;

/// <summary>Catégorie d'appareil détectée sur le bus USB.</summary>
public enum UsbDeviceKind
{
    /// <summary>Qualcomm HS-USB QDLoader 9008 (mode EDL).</summary>
    Edl9008,

    /// <summary>Appareil fastboot/adb (Xiaomi, Google, Intel, Nvidia…).</summary>
    Fastboot,
}

/// <summary>
/// Identité d'un appareil USB — remplace <c>UsbNodeConnectionInformation</c> +
/// parsing hub MiUSB.dll côté Windows. L'index appareil devient
/// <c>(bus, port physique)</c>, équivalent du calcul <c>rundll32 qcCoInstaller</c>.
/// </summary>
public sealed class UsbDeviceIdentity
{
    public ushort VendorId { get; init; }

    public ushort ProductId { get; init; }

    public byte BusNumber { get; init; }

    public byte PortNumber { get; init; }

    public byte[] PortNumbers { get; init; } = System.Array.Empty<byte>();

    public string SerialNumber { get; init; } = string.Empty;

    public string Product { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = string.Empty;

    public UsbDeviceKind Kind { get; init; }

    /// <summary>Identifiant physique stable : « bus.port1.port2… » (udev style).</summary>
    public string Location => BusNumber + "." + string.Join(".", PortNumbers.Length > 0 ? PortNumbers : new byte[] { PortNumber });

    /// <summary>Affichage « 05c6:9008 (bus 1, port 3) sn=… » (hexa minuscule, convention lsusb).</summary>
    public override string ToString() =>
        $"{VendorId:x4}:{ProductId:x4} (bus {BusNumber}, port {PortNumber})" +
        (string.IsNullOrEmpty(SerialNumber) ? string.Empty : $" sn={SerialNumber}");
}
