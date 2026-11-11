namespace MiFlash.Core;

/// <summary>
/// Cœur protocole du portage Linux de MiFlash — point d'entrée statique.
/// Jalon M1 : paquets Sahara (<see cref="Sahara"/>), commandes Firehose XML
/// (<see cref="Firehose"/>), formats d'images (<see cref="Images"/>),
/// <see cref="Transport.ITransport"/>, <see cref="Auth.IEdlAuthenticator"/>
/// (blob NoAuth statique), <see cref="Protocol.Comm"/> et
/// <see cref="Transfer.FileTransfer"/>.
/// </summary>
public static class MiFlashCore
{
    /// <summary>Version du portage Linux (semver, alignée sur les jalons).</summary>
    public const string Version = "0.1.0";
}
