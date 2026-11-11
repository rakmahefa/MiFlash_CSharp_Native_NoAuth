using System.Collections.Generic;
using System.Text;
using MiFlash.Core.Transport;

namespace MiFlash.Core.Tests.Fakes;

/// <summary>
/// Transport factice pour les tests sans matériel (RNF-5) : file de réponses
/// enregistrables, écritures capturées chunk par chunk.
/// </summary>
public sealed class FakeTransport : ITransport
{
    private readonly Queue<byte[]> _readQueue = new();

    public List<byte[]> WrittenChunks { get; } = new();

    public System.IO.MemoryStream Written { get; } = new();

    public bool IsOpen { get; private set; }

    public string PortName => "FAKE9008";

    public int BytesToRead => _readQueue.Count > 0 ? _readQueue.Peek().Length : 0;

    public int ReadTimeout { get; set; } = 2000;

    public int WriteTimeout { get; set; } = 2000;

    public void EnqueueResponse(string response) => EnqueueResponse(Encoding.UTF8.GetBytes(response));

    public void EnqueueResponse(byte[] bytes) => _readQueue.Enqueue(bytes);

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void DiscardBuffers() => _readQueue.Clear();

    /// <summary>No-op (transport de test sans matériel).</summary>
    public void ResetPort()
    {
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        byte[] chunk = _readQueue.Dequeue();
        System.Array.Copy(chunk, 0, buffer, offset, chunk.Length);
        return chunk.Length;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        byte[] copy = new byte[count];
        System.Array.Copy(buffer, offset, copy, 0, count);
        WrittenChunks.Add(copy);
        Written.Write(copy, 0, count);
    }

    public byte[] AllWritten => Written.ToArray();

    public void Dispose() => Close();
}
