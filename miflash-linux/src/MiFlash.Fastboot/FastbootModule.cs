namespace MiFlash.Fastboot;

/// <summary>
/// Exécution des ROM fastboot : exécuteur POSIX (remplacement de cmd.exe),
/// parsing natif des scripts flash_all*.bat/.sh, timeout et kill d'arborescence.
/// Implémentation apportée par le jalon M5 (todo.md).
/// </summary>
public static class FastbootModule
{
    /// <summary>Timeout global d'un script flash_all, porté de ScriptDevice.flash() (11 min 40 s).</summary>
    public static readonly TimeSpan GlobalScriptTimeout = TimeSpan.FromSeconds(11 * 60 + 40);
}
