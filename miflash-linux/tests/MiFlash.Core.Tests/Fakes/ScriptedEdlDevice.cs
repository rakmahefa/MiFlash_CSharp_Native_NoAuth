using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MiFlash.Core.Protocol;
using MiFlash.Core.Sahara;
using MiFlash.Core.Transport;

namespace MiFlash.Core.Tests.Fakes;

/// <summary>
/// Simulateur minimal d'une cible 9008 (Sahara + Firehose) pour tester le
/// moteur EDL sans matériel (RNF-5). Réagit aux écritures de l'hôte :
/// HELLO RESPONSE → READ DATA par blocs → END TRANSFER → DONE RESPONSE ;
/// commandes XML firehose → ACK (configure avec MaxPayloadSizeToTargetInBytes,
/// peek avec pattern DDR4 ou DDR5, program → double ACK groupé).
/// </summary>
public class ScriptedEdlDevice : ITransport
{
    private enum Phase { Idle, ReadData }

    private readonly Queue<byte> _toHost = new();
    private readonly List<byte[]> _hostWrites = new();
    private Phase _phase = Phase.Idle;
    private long _received;
    private readonly int _programmerChunkSize;
    private bool _awaitingAuthBlob;
    private bool _armHelloOnOpen;

    /// <summary>Taille du programmer attendu (l'appareil la connaît par le flux READ DATA).</summary>
    public int ProgrammerSize { get; set; } = 8192;

    /// <summary>Pattern renvoyé au peek CPUID : DDR4 ou DDR5.</summary>
    public string PeekResponse { get; set; } = "0xC3 0x00 0x40 0x10 ";

    /// <summary>Taille des données renvoyées par une commande &lt;read&gt; (backup).</summary>
    public int ReadDataSize { get; set; } = 4096;

    /// <summary>Octet de remplissage des données &lt;read&gt;.</summary>
    public byte ReadDataFill { get; set; } = 0xAB;

    /// <summary>Log « UFS Inquiry Command Output » renvoyé au getStorageInfo
    /// (Firmwarewrite) : format NAME(8)+VERSION(16)+NUMBER(4) = 28 caractères.</summary>
    public string StorageInfoInquiry { get; set; } =
        "INFO: UFS Inquiry Command Output: MNVNNX4Q1ABCDEFGHIJKLMNO1234";

    /// <summary>Numéro de série émis dans les logs configure (chipNum).</summary>
    public string ChipSerialLog { get; set; } = "(0123456789ABCDEF)";

    /// <summary>Vrai → la cible annonce quick_reset (partial_reset supporté).</summary>
    public bool SupportsQuickReset { get; set; }

    public bool IsOpen { get; private set; }

    public string PortName => "SIM9008";

    public int ReadTimeout { get; set; } = 2000;

    public int WriteTimeout { get; set; } = 2000;

    public virtual int BytesToRead => _toHost.Count;

    public IReadOnlyList<byte[]> HostWrites => _hostWrites;

    public ScriptedEdlDevice()
    {
        _programmerChunkSize = 4096;
        // La cible envoie son HELLO dès le branchement.
        EnqueueHello();
    }

    public void Open()
    {
        IsOpen = true;
        if (_armHelloOnOpen)
        {
            // Réaliste : l'appareil ne renvoie son HELLO qu'après le reset_to_edl
            // (réouverture du port par l'hôte), pas dans la même lecture que l'ACK.
            _armHelloOnOpen = false;
            _phase = Phase.Idle;
            EnqueueHello();
        }
    }

    public void Close() => IsOpen = false;

    public void DiscardBuffers() => _toHost.Clear();

    public virtual int Read(byte[] buffer, int offset, int count)
    {
        int n = 0;
        while (n < count && _toHost.Count > 0)
        {
            buffer[offset + n++] = _toHost.Dequeue();
        }
        return n;
    }

    public virtual void Write(byte[] buffer, int offset, int count)
    {
        var chunk = new byte[count];
        Array.Copy(buffer, offset, chunk, 0, count);
        _hostWrites.Add(chunk);

        // Commande XML firehose ?
        if (count > 5 && chunk[0] == (byte)'<' && chunk[1] == (byte)'?')
        {
            HandleXml(Encoding.UTF8.GetString(chunk));
            return;
        }

        uint command = BitConverter.ToUInt32(chunk, 0);
        if (command == SaharaCommand.SAHARA_DONE_PACKET && count == 8)
        {
            var done = new SaharaPackets.sahara_done_response { Command = SaharaCommand.SAHARA_DONE_RESPONSE, Length = 12, Status = 0 };
            Enqueue(CommandFormat.StructToBytes(done, 12));
            _phase = Phase.Idle;
            return;
        }
        if (_phase == Phase.Idle && command == SaharaCommand.SAHARA_HELLO_RESPONSE && count >= 48)
        {
            _phase = Phase.ReadData;
            _received = 0;
            EnqueueReadData();
            return;
        }
        if (_awaitingAuthBlob)
        {
            // Blob d'authentification NoAuth (256 o) : ACK puis fin d'attente.
            _awaitingAuthBlob = false;
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
            return;
        }
        if (_phase == Phase.ReadData)
        {
            // Données du programmer : demander la suite ou terminer.
            _received += count;
            if (_received < ProgrammerSize)
            {
                EnqueueReadData();
            }
            else
            {
                var end = new SaharaPackets.sahara_end_transfer_packet { Command = SaharaCommand.SAHARA_END_TRANSFER_PACKET, Length = 16, Image_id = 13, Status = 0 };
                Enqueue(CommandFormat.StructToBytes(end, 16));
            }
        }
    }

    private void EnqueueReadData()
    {
        long remaining = ProgrammerSize - _received;
        uint length = (uint)Math.Min(_programmerChunkSize, remaining);
        var rd = new SaharaPackets.sahara_readdata_packet
        {
            Command = SaharaCommand.SAHARA_READDATA_PACKET,
            Length = 20,
            Image_id = 13,
            Offset = (uint)_received,
            SLength = length,
        };
        Enqueue(CommandFormat.StructToBytes(rd, 20));
    }

    private void EnqueueHello()
    {
        var hello = new SaharaPackets.sahara_hello_packet
        {
            Command = SaharaCommand.SAHARA_HELLO_PACKET,
            Length = 48,
            Version = 2,
            Version_min = 1,
            Max_Command_Length = 256,
            Mode = 3,
            Reserved = new uint[6],
        };
        Enqueue(CommandFormat.StructToBytes(hello, 48));
    }

    private void HandleXml(string xml)
    {
        if (xml.Contains("<configure"))
        {
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\" MaxPayloadSizeToTargetInBytes=\"1048576\"/></data>");
            // Logs réalistes : numéro de série + quick_reset éventuel.
            if (!string.IsNullOrEmpty(ChipSerialLog))
            {
                EnqueueText("<?xml version=\"1.0\" ?><data><log value=\"Chip serial num " + ChipSerialLog + "\"/></data>");
            }
            if (SupportsQuickReset)
            {
                EnqueueText("<?xml version=\"1.0\" ?><data><log value=\"INFO: quick_reset supported\"/></data>");
            }
        }
        else if (xml.Contains("<peek"))
        {
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/><log value=\"INFO: " + PeekResponse + "\"/></data>");
        }
        else if (xml.Contains("<sig"))
        {
            // AUTHP : ACK puis attente du blob brut de 256 octets.
            _awaitingAuthBlob = true;
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
        }
        else if (xml.Contains("<getStorageInfo"))
        {
            // Firmwarewrite : ACK + inquiry UFS (name/version/number).
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/><log value=\""
                + StorageInfoInquiry + "\"/></data>");
        }
        else if (xml.Contains("<read "))
        {
            // Backup : ACK rawmode=true + données brutes + ACK rawmode=false
            // (cf. FileTransfer.ReadFile — parsing du premier/mode dernier paquet).
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\" rawmode=\"true\"/></data>");
            var data = new byte[ReadDataSize];
            Array.Fill(data, ReadDataFill);
            Enqueue(data);
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\" rawmode=\"false\"/></data>");
        }
        else if (xml.Contains("<program") || xml.Contains("<firmwarewrite"))
        {
            // 1 chunk écrit → 2 ACK groupés (données + statut), cf. chkRspAck
            // (program ET firmwarewrite, tous deux chunkCount=1 → 2 ACK requis).
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
        }
        else if (xml.Contains("<power") && xml.Contains("reset_to_edl"))
        {
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
            _phase = Phase.Idle;
            _armHelloOnOpen = true; // HELLO après réouverture (reboot EDL)
        }
        else
        {
            // nop, setbootablestoragedrive, patch, partial_reset, ufs
            // (provision), getsha256digest, firmwarewrite…
            EnqueueText("<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");
        }
    }

    private void EnqueueText(string text) => Enqueue(Encoding.UTF8.GetBytes(text));

    private void Enqueue(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            _toHost.Enqueue(b);
        }
    }

    /// <summary>Accès protégé au tampon hôte (sous-classes de test).</summary>
    protected void EnqueueToHostText(string text)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            _toHost.Enqueue(b);
        }
    }

    public void Dispose() => Close();
}
