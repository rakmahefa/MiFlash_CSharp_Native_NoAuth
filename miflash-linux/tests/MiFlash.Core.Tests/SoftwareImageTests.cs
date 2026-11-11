using System.Text.RegularExpressions;
using MiFlash.Core.Images;
using Xunit;

namespace MiFlash.Core.Tests;

public class SoftwareImageTests
{
    [Theory]
    [InlineData("rawprogram0.xml", true)]
    [InlineData("rawprogram12.xml", true)]
    [InlineData("rawprogram_backup.xml", false)]
    [InlineData("patch0.xml", true)]
    [InlineData("patch345.xml", true)]
    [InlineData("patched.xml", false)]
    public void RawProgram_and_Patch_patterns_classify_correctly(string file, bool expected)
    {
        bool raw = Regex.IsMatch(file, SoftwareImage.RawProgramPattern);
        bool patch = Regex.IsMatch(file, SoftwareImage.PatchPattern);
        if (file.StartsWith("rawprogram"))
        {
            Assert.Equal(expected, raw);
            Assert.False(patch);
        }
        else
        {
            Assert.Equal(expected, patch);
            Assert.False(raw);
        }
    }

    [Theory]
    [InlineData("prog_ufs_firehose_sm8250_ddr_4.elf", true)]
    [InlineData("prog_emmc_firehose_sm8150.elf", true)]
    [InlineData("prog_firehose_lite.elf", true)]
    [InlineData("xbl_gheto_devprg_firehose.elf", true)]
    [InlineData("boot.img", false)]
    public void ProgrammerPattern_matches_firehose_loaders(string file, bool expected)
    {
        Assert.Equal(expected, Regex.IsMatch(file, SoftwareImage.ProgrammerPattern));
    }

    [Fact]
    public void ProgrammerSelection_by_ddr_type()
    {
        Assert.Equal("prog_firehose_lite.elf", SoftwareImage.ProgrammerLite);
        Assert.Equal("prog_ufs_firehose_sm8250_ddr_4.elf", SoftwareImage.ProgrammerDDR4);
        Assert.Equal("prog_ufs_firehose_sm8250_ddr_5.elf", SoftwareImage.ProgrammerDDR5);
    }

    [Fact]
    public void ProvisionPattern_matches()
    {
        Assert.Matches(SoftwareImage.ProvisionPattern, "provision_autosecure.xml");
        Assert.DoesNotMatch(SoftwareImage.ProvisionPattern, "rawprogram0.xml");
    }

    [Fact]
    public void BackupRestore_patterns_are_distinct()
    {
        Assert.Matches("rawprogram_backup\\.xml", "rawprogram_backup.xml");
        Assert.Matches(SoftwareImage.RawRestorePattern, "rawprogram_restore.xml");
    }

    [Fact]
    public void DummyProgress_keeps_original_weights()
    {
        var progress = SoftwareImage.DummyProgress;
        Assert.Equal(23, progress.Count);
        Assert.Equal(1, progress["xbl"]);
        Assert.Equal(16, progress["system"]);
        Assert.Equal(34, progress["userdata"]);
        Assert.Equal(45, progress["cust"]);
    }
}
