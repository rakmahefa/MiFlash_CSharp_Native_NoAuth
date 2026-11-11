namespace MiFlash.Core;

/// <summary>
/// Cœur protocole du portage Linux de MiFlash — point d'entrée statique.
/// Contenu réel apporté par le jalon M1 (todo.md) : paquets Sahara,
/// commandes Firehose XML, formats d'images (sparse, rawprogram/patch),
/// <c>ITransport</c> et <c>IEdlAuthenticator</c>.
/// </summary>
public static class MiFlashCore
{
    /// <summary>Version du portage Linux (semver, alignée sur les jalons).</summary>
    public const string Version = "0.1.0";
}
