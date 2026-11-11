using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using MiFlash.Transport.Usb;

namespace MiFlash.Fastboot;

/// <summary>
/// Localisation et détection fastboot (M5.2) — remplace le fastboot.exe
/// embarqué <c>Source\ThirdParty\Google\Android</c> : le binaire est cherché
/// sur PATH (paquet <c>fastboot</c>/<c>android-tools</c>). <c>fastboot
/// devices -l</c> remplace <c>UsbDevice.GetScriptDevice()</c> (WMI), avec
/// corrélation optionnelle vers les identités libusb (serial ↔ bus.port).
/// </summary>
public static partial class FastbootCli
{
    /// <summary>Cherche le binaire fastboot sur PATH (DoctorService.FindOnPath est côté CLI).</summary>
    public static string LocateFastboot()
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), "fastboot");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // répertoire illisible : continuer
            }
        }
        return null;
    }

    /// <summary>Sortie de <c>fastboot devices -l</c> : couples (serial, description).</summary>
    public static List<(string Serial, string Description)> ListDevices(string fastbootPath, TimeSpan? timeout = null)
    {
        var result = new List<(string, string)>();
        if (fastbootPath == null)
        {
            return result;
        }
        // ProcessRunner sans statut (deviceName "fastboot") : sonde utilitaire.
        string output = Probe(fastbootPath, "devices -l", timeout ?? ProcessRunner.DefaultProbeTimeout);
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }
            // « serial\tusb:1-3 product:x product_variant:y device:z … »
            Match match = DeviceLineRegex().Match(line);
            if (match.Success)
            {
                result.Add((match.Groups[1].Value, line));
            }
        }
        return result;
    }

    /// <summary>Sonde : exécute et capture la sortie (sans FlashStatus, avec Log).</summary>
    private static string Probe(string fastbootPath, string arguments, TimeSpan timeout)
    {
        var si = new ProcessStartInfo
        {
            FileName = fastbootPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        try
        {
            using var process = Process.Start(si);
            if (process == null)
            {
                return string.Empty;
            }
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit((int)timeout.TotalMilliseconds);
            return output;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Corrélation fastboot ↔ libusb : enrichit les serials fastboot
    /// avec la localisation USB (bus.port) des identités M2.1.</summary>
    public static Dictionary<string, UsbDeviceIdentity> CorrelateWithUsb(IEnumerable<UsbDeviceIdentity> usbIdentities)
    {
        var bySerial = new Dictionary<string, UsbDeviceIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach (UsbDeviceIdentity identity in usbIdentities.Where(i => i.Kind == UsbDeviceKind.Fastboot && !string.IsNullOrEmpty(i.SerialNumber)))
        {
            bySerial[identity.SerialNumber] = identity;
        }
        return bySerial;
    }

    [GeneratedRegex(@"^(\S+)\s+(.*)$")]
    private static partial Regex DeviceLineRegex();
}
