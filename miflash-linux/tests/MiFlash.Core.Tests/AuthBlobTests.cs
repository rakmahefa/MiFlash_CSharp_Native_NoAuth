using MiFlash.Core.Auth;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>Golden tests du blob NoAuth — extraction programmatique depuis
/// SerialPortDevice.dlAuth() (commit 5450f4f).</summary>
public class AuthBlobTests
{
    // Empreinte calculée sur le tableau extrait du code Windows source.
    private const string ExpectedSha256 = "de84d23c7bfe2b0d2de488bc33a64081109c7b240315f5c250db5561bebae739";

    [Fact]
    public void Blob_is_256_bytes()
    {
        Assert.Equal(256, StaticBlobAuthenticator.AuthBlob.Length);
        var authenticator = new StaticBlobAuthenticator();
        Assert.Equal(256, authenticator.BlobSize);
    }

    [Fact]
    public void Blob_endpoints_match_source()
    {
        Assert.Equal(0xBF, StaticBlobAuthenticator.AuthBlob[0]);
        Assert.Equal(0x91, StaticBlobAuthenticator.AuthBlob[^1]);
    }

    [Fact]
    public void Blob_sha256_is_stable()
    {
        Assert.Equal(ExpectedSha256, StaticBlobAuthenticator.Sha256Hex());
    }

    [Fact]
    public void GetAuthBlob_returns_defensive_clone()
    {
        var authenticator = new StaticBlobAuthenticator();
        byte[] blob = authenticator.GetAuthBlob();
        blob[0] = 0x00;
        Assert.Equal(0xBF, authenticator.GetAuthBlob()[0]);
        Assert.Equal(0xBF, StaticBlobAuthenticator.AuthBlob[0]);
    }

    [Fact]
    public void Authenticator_satisfies_interface_contract()
    {
        IEdlAuthenticator authenticator = new StaticBlobAuthenticator();
        Assert.Equal(authenticator.BlobSize, authenticator.GetAuthBlob().Length);
    }
}
