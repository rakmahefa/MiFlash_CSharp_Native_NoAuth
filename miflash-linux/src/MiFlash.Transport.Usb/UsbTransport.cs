using System;
using System.Collections.Generic;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using MiFlash.Core.Transport;

namespace MiFlash.Transport.Usb;

/// <summary>
/// Transport bulk libusb vers le 9008 (M2.2) — implémente <see cref="ITransport"/>
/// en remplacement du port série Windows (driver qcusbser). Découverte
/// automatique des endpoints bulk IN/OUT de l'interface claimée, sémantique
/// « bytes disponibles » émulée par un tampon interne (libusb n'expose pas de
/// compteur d'octets en attente), ZLP optionnel pour <c>ZlpAwareHost="1"</c>.
/// </summary>
public sealed class UsbTransport : ITransport
{
    private readonly UsbDevice _device;
    private readonly int _interfaceNumber;
    private UsbEndpointReader _reader;
    private UsbEndpointWriter _writer;
    private readonly Queue<byte> _pending = new Queue<byte>();
    private int _outMaxPacketSize = 512;
    private bool _disposed;

    /// <summary>
    /// Émet un paquet de longueur nulle après une écriture multiple de la taille
    /// max de paquet — requis par les loaders firehose « ZlpAwareHost ».
    /// </summary>
    public bool SendZeroLengthPacketOnMultipleOfMaxPacket { get; set; } = true;

    public int ReadTimeout { get; set; } = 2000;

    public int WriteTimeout { get; set; } = 2000;

    public string PortName => $"{_device.VendorId:X4}:{_device.ProductId:X4} bus {_device.BusNumber} port {_device.PortNumber}";

    public bool IsOpen => _device.IsOpen;

    /// <summary>Nombre d'octets immédiatement disponibles (drain non bloquant).</summary>
    public int BytesToRead
    {
        get
        {
            ThrowIfNotOpen();
            DrainPending(timeout: 0);
            return _pending.Count;
        }
    }

    public UsbTransport(UsbDevice device, int interfaceNumber = 0)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _interfaceNumber = interfaceNumber;
    }

    public void Open()
    {
        if (_device.IsOpen)
        {
            return;
        }
        _device.Open();
        try
        {
            // Linux : détache qcserial/usbdm si besoin puis re-attache à la fermeture.
            _device.SetAutoDetachKernelDriver(true);
        }
        catch (Exception)
        {
            // Non supporté sur certaines plateformes : non bloquant.
        }
        if (!_device.ClaimInterface(_interfaceNumber))
        {
            throw new InvalidOperationException(
                $"Impossible de claimer l'interface {_interfaceNumber} de {_device.VendorId:X4}:{_device.ProductId:X4} " +
                "(permissions udev ? appareil occupé ?)");
        }
        DiscoverEndpoints();
    }

    private void DiscoverEndpoints()
    {
        byte? bulkIn = null;
        byte? bulkOut = null;
        ushort outMaxPacket = 512;
        foreach (var iface in _device.ActiveConfigDescriptor.Interfaces)
        {
            if (iface.Number != _interfaceNumber)
            {
                continue;
            }
            foreach (var endpoint in iface.Endpoints)
            {
                bool isBulk = (endpoint.Attributes & 0x03) == 2; // bits 0-1 : 00=control, 01=iso, 10=bulk, 11=interrupt
                if (!isBulk)
                {
                    continue;
                }
                bool isIn = (endpoint.EndpointAddress & 0x80) != 0;
                if (isIn && !bulkIn.HasValue)
                {
                    bulkIn = endpoint.EndpointAddress;
                }
                else if (!isIn && !bulkOut.HasValue)
                {
                    bulkOut = endpoint.EndpointAddress;
                    outMaxPacket = endpoint.MaxPacketSize == 0 ? (ushort)512 : endpoint.MaxPacketSize;
                }
            }
        }
        if (!bulkIn.HasValue || !bulkOut.HasValue)
        {
            throw new InvalidOperationException(
                $"Aucun endpoint bulk IN/OUT sur l'interface {_interfaceNumber} — appareil 9008 attendu.");
        }
        _outMaxPacketSize = outMaxPacket;
        var readId = (ReadEndpointID)(bulkIn.Value & 0x0F);
        var writeId = (WriteEndpointID)(bulkOut.Value & 0x0F);
        _reader = _device.OpenEndpointReader(readId, 0, EndpointType.Bulk);
        _writer = _device.OpenEndpointWriter(writeId, EndpointType.Bulk);
    }

    public void Close()
    {
        if (!_device.IsOpen)
        {
            return;
        }
        _reader = null;
        _writer = null;
        _pending.Clear();
        _device.Close();
    }

    /// <summary>Purge le tampon interne et le pipe IN du périphérique.</summary>
    public void DiscardBuffers()
    {
        _pending.Clear();
        _reader?.ReadFlush();
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfNotOpen();
        if (count == 0)
        {
            return 0;
        }
        // 1) servir depuis le tampon interne (données déjà drainées)
        if (_pending.Count == 0)
        {
            DrainPending(ReadTimeout);
        }
        int copied = 0;
        while (copied < count && _pending.Count > 0)
        {
            buffer[offset + copied++] = _pending.Dequeue();
        }
        return copied;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        ThrowIfNotOpen();
        Error error = _writer.Write(new ReadOnlySpan<byte>(buffer, offset, count), WriteTimeout, out int transferred);
        if (error != Error.Success)
        {
            if (error == Error.Timeout)
            {
                throw new TimeoutException($"écriture USB timeout ({WriteTimeout} ms)");
            }
            throw new System.IO.IOException($"écriture USB échouée : {error}");
        }
        if (SendZeroLengthPacketOnMultipleOfMaxPacket && NeedsZeroLengthPacket(count, _outMaxPacketSize))
        {
            _writer.Write(ReadOnlySpan<byte>.Empty, 0, out _);
        }
    }

    /// <summary>Un multiple exact de wMaxPacketSize doit être terminé par un ZLP.</summary>
    public static bool NeedsZeroLengthPacket(int count, int maxPacketSize)
        => count > 0 && maxPacketSize > 0 && count % maxPacketSize == 0;

    /// <summary>Lit ce qui est disponible sans bloquer (timeout=0) et l'accumule dans le tampon.</summary>
    private void DrainPending(int timeout)
    {
        var scratch = new byte[64 * 1024];
        while (_pending.Count < scratch.Length)
        {
            Error error = _reader.Read(scratch, timeout, out int read);
            if (error == Error.Success && read > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    _pending.Enqueue(scratch[i]);
                }
                if (timeout == 0 && read < scratch.Length)
                {
                    return; // plus rien d'immédiatement disponible
                }
                if (read == scratch.Length)
                {
                    continue; // tampon plein : continuer à drainer
                }
                return;
            }
            if (error == Error.Timeout)
            {
                return;
            }
            if (error == Error.NoDevice)
            {
                throw new System.IO.IOException("appareil débranché (NoDevice)");
            }
            throw new System.IO.IOException($"lecture USB échouée : {error}");
        }
    }

    private void ThrowIfNotOpen()
    {
        if (!_device.IsOpen)
        {
            throw new InvalidOperationException("transport USB non ouvert — appeler Open() d'abord.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            Close();
        }
        catch (Exception)
        {
        }
        _device.Dispose();
    }
}
