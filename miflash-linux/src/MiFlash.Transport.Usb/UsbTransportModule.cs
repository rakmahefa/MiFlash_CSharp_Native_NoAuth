namespace MiFlash.Transport.Usb;

/// <summary>
/// Transport USB (libusb-1.0 via LibUsbDotNet) pour le mode EDL 9008 (05c6:9008)
/// et les interfaces fastboot. Implémentation apportée par le jalon M2 (todo.md) :
/// énumération, claim d'interface, bulk IN/OUT, ZLP, hotplug.
/// </summary>
public static class UsbTransportModule
{
    /// <summary>VID/PID Qualcomm HS-USB QDLoader 9008 (mode EDL).</summary>
    public const int EdlVendorId = 0x05C6;

    /// <summary>PID du port Diag/QDLoader 9008.</summary>
    public const int EdlProductId = 0x9008;
}
