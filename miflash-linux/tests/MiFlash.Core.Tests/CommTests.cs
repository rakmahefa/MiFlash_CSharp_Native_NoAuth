using System.Text;
using MiFlash.Core.Protocol;
using MiFlash.Core.Runtime;
using MiFlash.Core.Tests.Fakes;
using MiFlash.Core.Transport;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>Tests comportementaux de Comm sur FakeTransport (M1.3/M1.7).</summary>
public class CommTests
{
    private static (Comm Comm, FakeTransport Transport) CreateComm()
    {
        // Journalisation silencieuse pendant les tests.
        Log.Sink = static (_, _) => { };
        var transport = new FakeTransport();
        transport.Open();
        return (new Comm(transport), transport);
    }

    [Fact]
    public void SendCommand_writes_utf8_bytes()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        const string command = "<?xml version=\"1.0\" ?><data><nop verbose=\"1\" value=\"ping\"/></data>";

        comm.SendCommand(command);

        byte[] written = transport.AllWritten;
        Assert.Equal(Encoding.UTF8.GetBytes(command), written);
    }

    [Fact]
    public void GetResponse_detects_ack_and_payload_size()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        comm.intSectorSize = 4096;
        comm.needEdlAuth = false;
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\" MaxPayloadSizeToTargetInBytes=\"1048576\"/></data>");

        bool ack = comm.GetResponse(waiteACK: true);

        Assert.True(ack);
        Assert.Equal(256, comm.m_dwBufferSectors); // 1048576 / 4096
    }

    [Fact]
    public void GetResponse_flags_needEdlAuth_on_noauth_error()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        comm.needEdlAuth = false;
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"NAK\" rawmode=\"false\" />" +
            "<log value=\"error: only nop and sig tag can be recevied before authentication\"/></data>");

        comm.GetResponse(waiteACK: true);

        Assert.True(comm.needEdlAuth);
    }

    [Theory]
    [InlineData("0xC3 0x00 0x40 0x10 ", true, false)]
    [InlineData("0xC3 0x00 0x31 0x10 ", true, false)]
    [InlineData("0xC3 0x00 0x02 0x10 ", true, false)]
    [InlineData("0xC3 0x00 0x00 0x10 ", false, true)]
    [InlineData("0xC3 0x00 0x51 0x10 ", false, true)]
    [InlineData("0xC3 0x00 0x32 0x10 ", false, true)]
    public void GetResponse_detects_ddr_generation(string peek, bool ddr4, bool ddr5)
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/><log value=\"INFO: peek " + peek + "\"/></data>");

        comm.GetResponse(waiteACK: true);

        Assert.Equal(ddr4, comm.isDdr4);
        Assert.Equal(ddr5, comm.isDdr5);
    }

    [Fact]
    public void GetResponse_parses_chip_serial_with_padding()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/>" +
            "<log value=\"INFO: Chip serial num (0x123456)\"/></data>");

        comm.GetResponse(waiteACK: true);

        Assert.True(comm.isGetChipNum);
        Assert.Equal("0x00123456", comm.chipNum); // complété à 10 caractères
    }

    [Fact]
    public void GetResponse_accepts_nak_maxpayload_16384_fallback()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"WARN: NAK: MaxPayloadSizeToTargetInBytes sent by host 1048576 larger than supported 16384\"/></data>");

        bool ack = comm.GetResponse(waiteACK: true);

        Assert.True(ack); // le NAK 16384 est traité comme un succès (Comm.cs l.374)
    }

    [Fact]
    public void ChkRspAck_counts_grouped_acks()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        // Un chunk ACK => le protocole renvoie 2 ACK groupés (data + status).
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>" +
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");

        bool ok = comm.chkRspAck(out string msg, 1);

        Assert.True(ok, msg);
    }

    [Fact]
    public void GetResponse_handles_multiple_xml_documents_in_one_read()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        transport.EnqueueResponse(
            "<?xml version=\"1.0\" ?><data><log value=\"INFO: hello\"/></data>" +
            "<?xml version=\"1.0\" ?><data><response value=\"ACK\"/></data>");

        bool ack = comm.GetResponse(waiteACK: true);

        Assert.True(ack);
    }

    [Fact]
    public void Constructor_requires_transport()
    {
        Assert.Throws<System.ArgumentNullException>(() => new Comm(null!));
    }

    [Fact]
    public void PortName_delegates_to_transport()
    {
        (Comm comm, FakeTransport transport) = CreateComm();
        Assert.Equal("FAKE9008", comm.PortName);
        Assert.Equal("FAKE9008", comm.transport.PortName);
    }
}
