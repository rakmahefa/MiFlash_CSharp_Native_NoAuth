namespace MiFlash.Transport.Usb;

/// <summary>
/// Filtres de détection — port fidèle de <c>UsbDevice.GetAndroidDevices()</c>
/// (l. 248) : un appareil fastboot doit avoir un champ Manufacturer non nul ET
/// matcher l'un des critères (produit android/fastboot/intel android ad,
/// fabricant « xiaomi inc », ou VID connu). Le 9008 est détecté par
/// VID/PID exacts.
/// </summary>
public static class DeviceFilter
{
    /// <summary>VID Qualcomm.</summary>
    public const ushort EdlVendorId = 0x05C6;

    /// <summary>PID QDLoader 9008 (mode EDL).</summary>
    public const ushort EdlProductId = 0x9008;

    /// <summary>VIDs des appareils fastboot/adb (Xiaomi, Google, Qualcomm, Intel, Nvidia, Spreadtrum).</summary>
    public static readonly ushort[] FastbootVendorIds = { 0x2717, 0x18D1, 0x05C6, 0x8087, 0x0955, 0x31EF };

    public static bool IsEdlDevice(ushort vendorId, ushort productId)
        => vendorId == EdlVendorId && productId == EdlProductId;

    /// <summary>Sémantique du code Windows : Manufacturer non nul requis, puis produit/VID/fabricant.</summary>
    public static bool IsFastbootDevice(string product, string manufacturer, ushort vendorId)
    {
        if (manufacturer == null)
        {
            return false;
        }
        string p = (product ?? string.Empty).ToLowerInvariant();
        return p == "android"
            || p == "fastboot"
            || p == "intel android ad"
            || manufacturer.ToLowerInvariant().Contains("xiaomi inc")
            || IsKnownFastbootVendor(vendorId);
    }

    public static bool IsKnownFastbootVendor(ushort vendorId)
        => System.Array.IndexOf(FastbootVendorIds, vendorId) >= 0;

    /// <summary>Classe un couple VID/PID (9008 uniquement par identifiants).</summary>
    public static UsbDeviceKind? Classify(ushort vendorId, ushort productId)
    {
        if (IsEdlDevice(vendorId, productId))
        {
            return UsbDeviceKind.Edl9008;
        }
        return null;
    }
}
