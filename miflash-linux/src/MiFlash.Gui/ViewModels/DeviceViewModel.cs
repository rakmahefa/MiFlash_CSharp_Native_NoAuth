namespace MiFlash.Gui.ViewModels;

/// <summary>
/// Appareil affiché dans la liste (M6.3) : identité USB (EDL 9008 ou
/// fastboot) + progression/statut alimentés par <see cref="Services.GuiReporter"/>.
/// </summary>
public sealed class DeviceViewModel : ViewModelBase
{
    /// <summary>Étiquette affichée et clé du reporter (ex. « 9008@1.3#sn »).</summary>
    public string DisplayName { get; }

    /// <summary>Kind USB (Edl9008 ou Fastboot) pour filtrer les cibles.</summary>
    public bool IsEdl { get; }

    /// <summary>Localisation physique « bus.port » (rapprochement PortName).</summary>
    public string Location { get; }

    /// <summary>Numéro de série fastboot (cible -s).</summary>
    public string SerialNumber { get; }

    private double _progress;
    private string _statusText = "prêt";
    private bool _isRunning;

    public DeviceViewModel(string displayName, bool isEdl, string location, string serialNumber)
    {
        DisplayName = displayName;
        IsEdl = isEdl;
        Location = location ?? string.Empty;
        SerialNumber = serialNumber ?? string.Empty;
    }

    /// <summary>Progression 0–1 (ProgressBar).</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value < 0 ? 0 : value > 1 ? 1 : value, nameof(Progress));
    }

    /// <summary>Dernier message d'état du reporter.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value ?? string.Empty, nameof(StatusText));
    }

    /// <summary>Opération en cours sur cet appareil.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value, nameof(IsRunning));
    }

    /// <summary>Remet à zéro avant une opération.</summary>
    public void Reset()
    {
        Progress = 0;
        StatusText = "démarrage…";
        IsRunning = true;
    }

    /// <summary>Rapprochement tolérant d'un nom venu du reporter :
    /// le moteur signale tantôt le DeviceName (« 9008@1.3 »), tantôt le
    /// PortName du transport (« 05c6:9008 bus 1 port 3 »), tantôt le serial
    /// fastboot.</summary>
    public bool Matches(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (string.Equals(name, DisplayName, System.StringComparison.OrdinalIgnoreCase)
            || (SerialNumber.Length > 0 && string.Equals(name, SerialNumber, System.StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        // PortName : « 05c6:9008 bus 1 port 3 » ↔ Location « 1.3 ».
        if (Location.Length > 0 && name.Contains("bus ", System.StringComparison.OrdinalIgnoreCase))
        {
            string bus = null, port = null;
            string[] parts = name.Split(' ');
            for (int i = 0; i + 1 < parts.Length; i++)
            {
                if (parts[i].Equals("bus", System.StringComparison.OrdinalIgnoreCase))
                {
                    bus = parts[i + 1];
                }
                else if (parts[i].Equals("port", System.StringComparison.OrdinalIgnoreCase))
                {
                    port = parts[i + 1];
                }
            }
            if (bus != null && port != null
                && Location.StartsWith(bus + ".", System.StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
