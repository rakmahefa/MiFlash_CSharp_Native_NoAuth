using MiFlash.Core.Firehose;
using Xunit;

namespace MiFlash.Core.Tests;

public class FirehoseCommandsTests
{
    [Fact]
    public void Configure_carries_payload_zlp_and_storage()
    {
        string xml = string.Format(FirehoseCommands.Configure, "1", 1048576, "ufs", "0");
        Assert.Contains("MaxPayloadSizeToTargetInBytes=\"1048576\"", xml);
        Assert.Contains("MemoryName=\"ufs\"", xml);
        Assert.Contains("SkipStorageInit=\"0\"", xml);
        Assert.Contains("ZlpAwareHost=\"1\"", xml);
        Assert.Contains("AlwaysValidate=\"0\"", xml);
        Assert.StartsWith("<?xml version=\"1.0\" ?>", xml);
    }

    [Fact]
    public void Program_command_encodes_sector_layout()
    {
        string xml = string.Format(FirehoseCommands.FIREHOSE_PROGRAM, 4096, 100, 0, 1, "");
        Assert.Contains("<program", xml);
        Assert.Contains("SECTOR_SIZE_IN_BYTES=\"4096\"", xml);
        Assert.Contains("num_partition_sectors=\"100\"", xml);
        Assert.Contains("start_sector=\"0\"", xml);
        Assert.Contains("physical_partition_number=\"1\"", xml);
    }

    [Fact]
    public void Read_command_encodes_size_in_kb()
    {
        string xml = string.Format(FirehoseCommands.FIREHOSE_READ, 4096, 10, 20, 0, "40", "");
        Assert.Contains("<read", xml);
        Assert.Contains("size_in_KB=\"40\"", xml);
    }

    [Fact]
    public void Patch_command_carries_value_and_offset()
    {
        string xml = string.Format(FirehoseCommands.FIREHOSE_PATCH, 4096, 4, 0, 8, 16, "0x11223344", "");
        Assert.Contains("byte_offset=\"4\"", xml);
        Assert.Contains("start_sector=\"16\"", xml);
        Assert.Contains("value=\"0x11223344\"", xml);
        Assert.Contains("filename=\"DISK\"", xml);
    }

    [Fact]
    public void Auth_templates_match_noauth_flow()
    {
        Assert.Equal("<?xml version=\"1.0\" ?><data> <sig TargetName=\"req\" verbose=\"1\"/></data>", FirehoseCommands.REQ_AUTH);
        string authp = string.Format(FirehoseCommands.AUTHP, 256);
        Assert.Contains("TargetName=\"sig\"", authp);
        Assert.Contains("size_in_bytes=\"256\"", authp);
        string auth = string.Format(FirehoseCommands.AUTH, "DEADBEEF");
        Assert.Contains("value=\"DEADBEEF\"", auth);
    }

    [Fact]
    public void Power_commands_cover_reset_variants()
    {
        Assert.Contains("value=\"reset_to_edl\"", FirehoseCommands.Reset_To_Edl);
        Assert.Contains("DelayInSeconds=\"1\"", FirehoseCommands.Reset_To_Edl);
        Assert.Contains("value=\"partial_reset\"", FirehoseCommands.Partial_Reset);
        Assert.Contains("value=\"reset\"/>", FirehoseCommands.Reset);
    }

    [Fact]
    public void SetBootPartition_targets_ufs_lun1()
    {
        Assert.Contains("setbootablestoragedrive", FirehoseCommands.SetBootPartition);
        Assert.Contains("value=\"1\"", FirehoseCommands.SetBootPartition);
    }

    [Fact]
    public void Constants()
    {
        Assert.Equal(1048576, FirehoseCommands.payload_size);
        Assert.Equal(50, FirehoseCommands.MAX_PATCH_VALUE_LEN);
        Assert.Equal(1, FirehoseCommands.PROGRAMMER_LITE);
        Assert.Equal(2, FirehoseCommands.PROGRAMMER_DDR4);
        Assert.Equal(3, FirehoseCommands.PROGRAMMER_DDR5);
        Assert.Contains("address64=\"0x00780198\"", FirehoseCommands.PEEK);
        Assert.Contains("address64=\"0x00786134\"", FirehoseCommands.PEEKCPUID);
    }
}
