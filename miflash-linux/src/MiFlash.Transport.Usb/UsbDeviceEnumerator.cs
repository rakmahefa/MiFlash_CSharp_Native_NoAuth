using System;
using System.Collections.Generic;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;

namespace MiFlash.Transport.Usb;

/// <summary>
/// Énumération des appareils EDL/fastboot via libusb (M2.1) — remplace WMI
/// <c>Win32_PnPEntity</c>, <c>MiUSB.dll</c> et <c>lsusb.exe</c>. L'accès aux
/// descripteurs de chaînes (serial, produit) peut échouer si l'appareil est
/// déjà ouvert par un autre processus : l'identité reste alors retournée avec
/// les champs texte vides.
/// </summary>
public sealed class UsbDeviceEnumerator : IDisposable
{
    private readonly UsbContext _context;

    public UsbDeviceEnumerator()
    {
        _context = new UsbContext();
    }

    /// <summary>Liste tous les appareils EDL 9008 et fastboot présents.</summary>
    public List<UsbDeviceIdentity> ListDevices()
    {
        var result = new List<UsbDeviceIdentity>();
        foreach (UsbDevice device in _context.List())
        {
            UsbDeviceIdentity identity = TryIdentify(device);
            if (identity != null)
            {
                result.Add(identity);
            }
        }
        return result;
    }

    /// <summary>Ouvre un appareil EDL 9008 et renvoie son transport bulk (claim interface 0).</summary>
    public UsbTransport OpenEdlDevice(UsbDeviceIdentity identity)
    {
        if (identity == null)
        {
            throw new ArgumentNullException(nameof(identity));
        }
        if (identity.Kind != UsbDeviceKind.Edl9008)
        {
            throw new ArgumentException("L'appareil n'est pas un 9008 (EDL) : " + identity, nameof(identity));
        }
        foreach (UsbDevice device in _context.List())
        {
            if (device.VendorId == identity.VendorId
                && device.ProductId == identity.ProductId
                && device.BusNumber == identity.BusNumber
                && device.PortNumber == identity.PortNumber)
            {
                return new UsbTransport(device);
            }
        }
        throw new InvalidOperationException("Appareil EDL introuvable (débranché ?) : " + identity);
    }

    internal static UsbDeviceIdentity TryIdentify(UsbDevice device)
    {
        ushort vid = device.VendorId;
        ushort pid = device.ProductId;

        UsbDeviceKind kind;
        if (DeviceFilter.IsEdlDevice(vid, pid))
        {
            kind = UsbDeviceKind.Edl9008;
        }
        else
        {
            string product = null;
            string manufacturer = null;
            string serial = null;
            try
            {
                // La lecture des chaînes peut ouvrir l'appareil : tolérant aux échecs
                // (permissions udev, appareil occupé).
                product = device.Info?.Product;
                manufacturer = device.Info?.Manufacturer;
                serial = device.Info?.SerialNumber;
            }
            catch (Exception)
            {
            }
            if (!DeviceFilter.IsFastbootDevice(product, manufacturer, vid))
            {
                return null;
            }
            return new UsbDeviceIdentity
            {
                VendorId = vid,
                ProductId = pid,
                BusNumber = device.BusNumber,
                PortNumber = device.PortNumber,
                PortNumbers = SafePortNumbers(device),
                SerialNumber = serial ?? string.Empty,
                Product = product ?? string.Empty,
                Manufacturer = manufacturer ?? string.Empty,
                Kind = UsbDeviceKind.Fastboot,
            };
        }

        string edlSerial = null;
        try
        {
            edlSerial = device.Info?.SerialNumber;
        }
        catch (Exception)
        {
        }
        return new UsbDeviceIdentity
        {
            VendorId = vid,
            ProductId = pid,
            BusNumber = device.BusNumber,
            PortNumber = device.PortNumber,
            PortNumbers = SafePortNumbers(device),
            SerialNumber = edlSerial ?? string.Empty,
            Product = "QDLoader 9008",
            Manufacturer = "Qualcomm",
            Kind = kind,
        };
    }

    private static byte[] SafePortNumbers(UsbDevice device)
    {
        try
        {
            return System.Linq.Enumerable.ToArray(device.PortNumbers);
        }
        catch (Exception)
        {
            return new byte[] { device.PortNumber };
        }
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
