namespace MiFlash.Core.Transport;

/// <summary>
/// Abstraction du transport octet-vers-octet vers l'appareil (M1.2).
/// Implémentations : libusb bulk (MiFlash.Transport.Usb, M2), port série
/// qcserial (MiFlash.Transport.Serial, M2), faux transport de test.
/// Remplace <c>System.IO.Ports.SerialPort</c> utilisé côté Windows — le port
/// 9008 étant en pratique une interface bulk USB (spec §3, dépendance n° 2).
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>Nom logique du transport (ex. COM3 côté Windows, 05c6:9008 bus/port côté Linux).</summary>
    string PortName { get; }

    /// <summary>Transport ouvert.</summary>
    bool IsOpen { get; }

    /// <summary>Octets disponibles en réception (polling, équivalent SerialPort.BytesToRead).</summary>
    int BytesToRead { get; }

    /// <summary>Timeout de lecture en millisecondes.</summary>
    int ReadTimeout { get; set; }

    /// <summary>Timeout d'écriture en millisecondes.</summary>
    int WriteTimeout { get; set; }

    /// <summary>Ouvre le transport (claim de l'interface USB ou ouverture du tty).</summary>
    void Open();

    /// <summary>Ferme le transport.</summary>
    void Close();

    /// <summary>Purge les tampons IN et OUT (équivalent DiscardInBuffer + DiscardOutBuffer).</summary>
    void DiscardBuffers();

    /// <summary>
    /// Reset matériel du port (M6.6, fix POCO F3). Sur le 9008 : USBDEVFS_RESET
    /// via libusb — le PBL relance sa machine d'état Sahara et renvoie un HELLO
    /// frais. C'est la récupération quand le HELLO initial a été consommé par un
    /// driver noyau (qcserial/qcaux) lié à l'interface avant le claim libusb.
    /// No-op sur les transports sans équivalent matériel (série, faux de test).
    /// </summary>
    void ResetPort();

    /// <summary>Lit <paramref name="count"/> octets (bloquant jusqu'au timeout).</summary>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>Écrit <paramref name="count"/> octets.</summary>
    void Write(byte[] buffer, int offset, int count);
}
