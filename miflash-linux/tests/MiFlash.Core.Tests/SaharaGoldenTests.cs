using System;
using System.Runtime.InteropServices;
using MiFlash.Core.Protocol;
using MiFlash.Core.Sahara;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>Golden tests des paquets Sahara — tailles et valeurs du flux réel (M1.7).</summary>
public class SaharaGoldenTests
{
    [Fact]
    public void Packet_sizes_match_wire_layout()
    {
        Assert.Equal(8, Marshal.SizeOf<SaharaPackets.sahara_packet>());
        Assert.Equal(48, Marshal.SizeOf<SaharaPackets.sahara_hello_packet>());
        Assert.Equal(48, Marshal.SizeOf<SaharaPackets.sahara_hello_response>());
        Assert.Equal(20, Marshal.SizeOf<SaharaPackets.sahara_readdata_packet>());
        Assert.Equal(32, Marshal.SizeOf<SaharaPackets.sahara_64b_readdata_packet>());
        Assert.Equal(16, Marshal.SizeOf<SaharaPackets.sahara_end_transfer_packet>());
        Assert.Equal(12, Marshal.SizeOf<SaharaPackets.sahara_switch_Mode_packet>());
        Assert.Equal(12, Marshal.SizeOf<SaharaPackets.sahara_done_response>());
    }

    [Fact]
    public void Command_ids_match_protocol()
    {
        Assert.Equal(1, SaharaCommand.SAHARA_HELLO_PACKET);
        Assert.Equal(2, SaharaCommand.SAHARA_HELLO_RESPONSE);
        Assert.Equal(3, SaharaCommand.SAHARA_READDATA_PACKET);
        Assert.Equal(4, SaharaCommand.SAHARA_END_TRANSFER_PACKET);
        Assert.Equal(5, SaharaCommand.SAHARA_DONE_PACKET);
        Assert.Equal(6, SaharaCommand.SAHARA_DONE_RESPONSE);
        Assert.Equal(12, SaharaCommand.SAHARA_SWITCH_MODE_PACKET);
        Assert.Equal(18, SaharaCommand.SAHARA_64B_READDATA_PACKET);
    }

    /// <summary>Golden : un HELLO 48 octets émis par une cible 9008 est décodé correctement.</summary>
    [Fact]
    public void Device_hello_packet_decodes()
    {
        var hello = new SaharaPackets.sahara_hello_packet
        {
            Command = 1,
            Length = 48,
            Version = 2,
            Version_min = 1,
            Max_Command_Length = 256,
            Mode = 3,
            Reserved = new uint[6],
        };
        byte[] wire = CommandFormat.StructToBytes(hello, Marshal.SizeOf<SaharaPackets.sahara_hello_packet>());
        Assert.Equal(48, wire.Length);

        var decoded = CommandFormat.BytesToStuct<SaharaPackets.sahara_hello_packet>(wire);
        Assert.Equal(1u, decoded.Command);
        Assert.Equal(48u, decoded.Length);
        Assert.Equal(2u, decoded.Version);
        Assert.Equal(1u, decoded.Version_min);
        Assert.Equal(256u, decoded.Max_Command_Length);
        Assert.Equal(3u, decoded.Mode);
    }

    /// <summary>Golden : le HELLO RESPONSE émis par l'hôte (version 2 / min 1 / mode 3, cf. SaharaDownloadProgrammer l.425–433).</summary>
    [Fact]
    public void Host_hello_response_encodes()
    {
        var response = new SaharaPackets.sahara_hello_response
        {
            Reserved = new uint[6],
            Command = 2,
            Length = 48,
            Version = 2,
            Version_min = 1,
            Mode = 3,
        };
        byte[] wire = CommandFormat.StructToBytes(response);
        Assert.Equal(48, wire.Length);
        Assert.Equal(0x02, wire[0]);
        Assert.Equal(0x30, wire[4]);  // Length = 48 (0x30) en little-endian
        Assert.Equal(0x02, wire[8]);  // Version
        Assert.Equal(0x01, wire[12]); // Version_min
        Assert.Equal(0x00, wire[16]); // Status
        Assert.Equal(0x03, wire[20]); // Mode
    }

    [Fact]
    public void Switch_mode_packet_is_12_bytes_mode0()
    {
        var sw = new SaharaPackets.sahara_switch_Mode_packet { Command = 12, Length = 12, Mode = 0 };
        byte[] wire = CommandFormat.StructToBytes(sw, 12);
        Assert.Equal(12, wire.Length);
        Assert.Equal(0x0C, wire[0]);
        Assert.Equal(0x0C, wire[4]);
        Assert.Equal(0x00, wire[8]);
    }

    [Fact]
    public void Readdata_64b_carries_64bit_fields()
    {
        var rd = new SaharaPackets.sahara_64b_readdata_packet
        {
            Command = 18,
            Length = 32,
            Image_id = 0x1122334455667788UL,
            Offset = 0x1000UL,
            SLength = 0x100000UL,
        };
        byte[] wire = CommandFormat.StructToBytes(rd, 32);
        var decoded = CommandFormat.BytesToStuct<SaharaPackets.sahara_64b_readdata_packet>(wire);
        Assert.Equal(0x1122334455667788UL, decoded.Image_id);
        Assert.Equal(0x1000UL, decoded.Offset);
        Assert.Equal(0x100000UL, decoded.SLength);
    }

    [Fact]
    public void BytesToStuct_returns_null_when_buffer_too_short()
    {
        Assert.Null(CommandFormat.BytesToStuct(new byte[4], typeof(SaharaPackets.sahara_packet)));
    }
}
