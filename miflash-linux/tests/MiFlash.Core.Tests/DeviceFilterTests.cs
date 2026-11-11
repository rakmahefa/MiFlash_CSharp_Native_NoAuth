using MiFlash.Transport.Usb;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>Tests du filtre de détection — port fidèle de UsbDevice.GetAndroidDevices l.248.</summary>
public class DeviceFilterTests
{
    [Fact]
    public void Edl_is_05c6_9008_only()
    {
        Assert.True(DeviceFilter.IsEdlDevice(0x05C6, 0x9008));
        Assert.False(DeviceFilter.IsEdlDevice(0x05C6, 0x9006)); // diag autre
        Assert.False(DeviceFilter.IsEdlDevice(0x2717, 0x9008));
        Assert.False(DeviceFilter.IsEdlDevice(0x18D1, 0xD00D));
    }

    [Theory]
    [InlineData("android", "Xiaomi Inc.", 0x2717, true)]
    [InlineData("fastboot", "Xiaomi Inc.", 0x2717, true)]
    [InlineData("Android", "Google Inc.", 0x18D1, true)] // insensible à la casse
    [InlineData("FASTBOOT", "Google Inc.", 0x18D1, true)]
    [InlineData("intel android ad", "Intel Corp.", 0x8087, true)]
    [InlineData("whatever", "XIAOMI Inc", 0x1234, true)] // fabricant contient « xiaomi inc »
    [InlineData("whatever", "Other", 0x2717, true)]      // VID connu
    [InlineData("whatever", "Other", 0x31EF, true)]
    [InlineData("whatever", "Other", 0x0955, true)]
    [InlineData("whatever", "Other", 0x1234, false)]     // aucun critère
    [InlineData("gadget", "Other", 0x18D1, true)]        // VID Google reconnu
    public void Fastboot_detection_matches_windows_logic(string product, string manufacturer, ushort vid, bool expected)
    {
        Assert.Equal(expected, DeviceFilter.IsFastbootDevice(product, manufacturer, vid));
    }

    [Fact]
    public void Null_manufacturer_disqualifies_even_known_vid()
    {
        // Fidélité au code Windows : `Manufacturer != null && (…)`.
        Assert.False(DeviceFilter.IsFastbootDevice("android", null, 0x2717));
        Assert.False(DeviceFilter.IsFastbootDevice("android", null, 0x18D1));
    }

    [Fact]
    public void Known_fastboot_vendors()
    {
        Assert.Equal(6, DeviceFilter.FastbootVendorIds.Length);
        Assert.Contains((ushort)0x2717, DeviceFilter.FastbootVendorIds);
        Assert.Contains((ushort)0x18D1, DeviceFilter.FastbootVendorIds);
        Assert.Contains((ushort)0x05C6, DeviceFilter.FastbootVendorIds);
        Assert.Contains((ushort)0x8087, DeviceFilter.FastbootVendorIds);
        Assert.Contains((ushort)0x0955, DeviceFilter.FastbootVendorIds);
        Assert.Contains((ushort)0x31EF, DeviceFilter.FastbootVendorIds);
    }

    [Fact]
    public void Identity_location_and_display()
    {
        var id = new UsbDeviceIdentity
        {
            VendorId = 0x05C6,
            ProductId = 0x9008,
            BusNumber = 1,
            PortNumber = 3,
            PortNumbers = new byte[] { 2, 3 },
            SerialNumber = "SN123",
            Kind = UsbDeviceKind.Edl9008,
        };
        Assert.Equal("1.2.3", id.Location);
        Assert.Equal("05c6:9008 (bus 1, port 3) sn=SN123", id.ToString());

        var noSerial = new UsbDeviceIdentity { VendorId = 0x2717, ProductId = 0xFF48, BusNumber = 2, PortNumber = 1, PortNumbers = new byte[] { 1 } };
        Assert.Equal("2.1", noSerial.Location);
        Assert.Equal("2717:ff48 (bus 2, port 1)", noSerial.ToString());
    }

    [Fact]
    public void Zlp_is_needed_on_exact_multiple_of_max_packet()
    {
        Assert.True(UsbTransport.NeedsZeroLengthPacket(512, 512));
        Assert.True(UsbTransport.NeedsZeroLengthPacket(1048576, 512));
        Assert.False(UsbTransport.NeedsZeroLengthPacket(511, 512));
        Assert.False(UsbTransport.NeedsZeroLengthPacket(513, 512));
        Assert.False(UsbTransport.NeedsZeroLengthPacket(0, 512));
        Assert.False(UsbTransport.NeedsZeroLengthPacket(100, 0));
    }
}
