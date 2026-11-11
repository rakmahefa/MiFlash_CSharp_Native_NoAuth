namespace MiFlash.Core.Runtime;

/// <summary>
/// Signalement d'état d'un appareil pendant le flash — remplace
/// <c>FlashingDevice.UpdateDeviceStatus</c> (statique, couplé UI côté Windows).
/// </summary>
public interface IFlashStatusReporter
{
    /// <param name="deviceName">Nom/port logique de l'appareil.</param>
    /// <param name="progress">Progression 0–1 (null = inchangée).</param>
    /// <param name="status">Message d'état (null/vider = inchangé).</param>
    /// <param name="result">Résultat ("flashing", "error", "success"...).</param>
    /// <param name="isDone">Opération terminée pour cet appareil.</param>
    void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone);
}

/// <summary>Hub statique de signalement — les modules protocole appellent <see cref="UpdateDeviceStatus"/>.</summary>
public static class FlashStatus
{
    private static IFlashStatusReporter _reporter = new NoopReporter();

    /// <summary>Reporter actif (la CLI/GUI le remplace ; no-op par défaut).</summary>
    public static IFlashStatusReporter Reporter
    {
        get => _reporter;
        set => _reporter = value ?? new NoopReporter();
    }

    public static void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone)
    {
        _reporter.UpdateDeviceStatus(deviceName, progress, status, result, isDone);
    }

    private sealed class NoopReporter : IFlashStatusReporter
    {
        public void UpdateDeviceStatus(string deviceName, float? progress, string status, string result, bool isDone)
        {
        }
    }
}
