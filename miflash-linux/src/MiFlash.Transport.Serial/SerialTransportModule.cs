namespace MiFlash.Transport.Serial;

/// <summary>
/// Transport série de repli (module noyau qcserial → /dev/ttyUSB*) via System.IO.Ports.
/// Le port 9008 est en pratique une interface bulk USB : ce transport n'est qu'un
/// repli si libusb est indisponible (spec-linux.md §3, dépendance n° 2).
/// Implémentation apportée par le jalon M2 (todo.md M2.5).
/// </summary>
public static class SerialTransportModule
{
    /// <summary>Préfixe des périphériques série exposés par qcserial sur Linux.</summary>
    public const string LinuxSerialPrefix = "/dev/ttyUSB";
}
