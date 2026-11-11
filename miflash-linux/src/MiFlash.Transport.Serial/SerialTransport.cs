using System;
using System.IO.Ports;
using MiFlash.Core.Transport;

namespace MiFlash.Transport.Serial;

/// <summary>
/// Transport série de repli (M2.5) — <c>/dev/ttyUSB*</c> exposé par le module
/// noyau <c>qcserial</c> quand libusb n'est pas utilisé. Le baudrate est
/// ignoré en pratique (le « port » 9008 est une interface bulk USB), valeur
/// par défaut 9600 comme le code Windows d'origine.
/// </summary>
public sealed class SerialTransport : ITransport
{
    private readonly SerialPort _port;

    public SerialTransport(string portName, int baudRate = 9600)
    {
        if (string.IsNullOrEmpty(portName))
        {
            throw new ArgumentNullException(nameof(portName));
        }
        _port = new SerialPort(portName, baudRate)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            Handshake = Handshake.None,
        };
    }

    public string PortName => _port.PortName;

    public bool IsOpen => _port.IsOpen;

    public int BytesToRead => _port.IsOpen ? _port.BytesToRead : 0;

    public int ReadTimeout
    {
        get => _port.ReadTimeout;
        set => _port.ReadTimeout = value;
    }

    public int WriteTimeout
    {
        get => _port.WriteTimeout;
        set => _port.WriteTimeout = value;
    }

    public void Open() => _port.Open();

    public void Close() => _port.Close();

    public void DiscardBuffers()
    {
        if (_port.IsOpen)
        {
            _port.DiscardOutBuffer();
            _port.DiscardInBuffer();
        }
    }

    /// <summary>Pas d'équivalent matériel sur un tty : no-op volontaire
    /// (la récupération Sahara HELLO est une spécificité USB 9008).</summary>
    public void ResetPort()
    {
    }

    public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    public void Dispose() => _port.Dispose();
}
