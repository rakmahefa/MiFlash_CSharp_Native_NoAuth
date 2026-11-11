using MiFlash.Core;

namespace MiFlash.Core.Tests;

/// <summary>Tests de fumée du socle M0.3 — remplacés/étendus par les golden
/// tests protocole du jalon M1 (todo.md M1.7).</summary>
public class SolutionSmokeTests
{
    [Fact]
    public void Version_is_semver()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", MiFlashCore.Version);
    }

    [Fact]
    public void Core_assembly_resolves()
    {
        var assembly = typeof(MiFlashCore).Assembly;
        Assert.NotNull(assembly);
        Assert.EndsWith("MiFlash.Core", assembly.GetName().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Edl_constants_match_windows_source()
    {
        // Garde-fous de fidélité du portage : valeurs tirées du code Windows analysé.
        Assert.Equal(0x05C6, MiFlash.Transport.Usb.UsbTransportModule.EdlVendorId);
        Assert.Equal(0x9008, MiFlash.Transport.Usb.UsbTransportModule.EdlProductId);
        Assert.Equal(319, MiFlash.Edl.EdlEngine.StaticAuthBlobLength);
    }
}
