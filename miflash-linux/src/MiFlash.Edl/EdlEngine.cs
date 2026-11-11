namespace MiFlash.Edl;

/// <summary>
/// Moteur EDL complet (port de SerialPortDevice.cs) : orchestration
/// Sahara → Firehose — téléversement du programmer, auth NoAuth (blob statique),
/// rawprogram/patch, provision, backup/restore, resets.
/// Implémentation apportée par le jalon M3 (todo.md).
/// </summary>
public static class EdlEngine
{
    /// <summary>Taille du blob d'authentification NoAuth observée dans dlAuth() (319 octets).</summary>
    public const int StaticAuthBlobLength = 319;
}
