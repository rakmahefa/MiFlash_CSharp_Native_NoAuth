using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MiFlash.Cli.Doctor;
using MiFlash.Cli.Io;
using MiFlash.Core.Runtime;
using MiFlash.Core.Transport;
using MiFlash.Edl;
using MiFlash.Fastboot;
using MiFlash.Gui.Services;
using MiFlash.Transport.Usb;

namespace MiFlash.Gui.ViewModels;

/// <summary>
/// Orchestrateur du GUI (M6.3) : liste des appareils (EDL 9008 + fastboot),
/// sélection du répertoire ROM, lancement des opérations (flash EDL,
/// flash fastboot, backup, reboot, doctor) sur des threads de fond, via
/// <see cref="MultiDeviceFlasher"/>/<see cref="FastbootFlasher"/> —
/// exactement le même moteur que le CLI (M3.9/M5).
/// </summary>
/// <remarks>
/// Aucune dépendance Avalonia : le marshaling UI passe par
/// <c>uiPost</c> (injected), les sources de données par des délégués
/// remplacables dans les tests headless.
/// </remarks>
public sealed class MainViewModel : ViewModelBase
{
    private const int MaxLogLines = 2000;

    private readonly Action<Action> _uiPost;
    private readonly Func<List<UsbDeviceIdentity>> _deviceProvider;
    private readonly Func<IEnumerable<EdlJob>, IReadOnlyList<EdlJobResult>> _edlRunner;
    private readonly Func<string> _fastbootLocator;
    private readonly Func<List<(string Serial, string Description)>> _fastbootLister;
    private readonly GuiReporter _reporter = new();
    private readonly Dictionary<string, DeviceViewModel> _deviceIndex = new();
    private UsbDeviceWatcher _watcher;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    private DeviceViewModel _selectedDevice;
    private string _romDirectory = string.Empty;
    private int _programmerIndex;
    private bool _confirmed;
    private bool _watch;
    private bool _isBusy;

    /// <summary>Émis par « Parcourir… » — la fenêtre possède le StorageProvider.</summary>
    public event Action BrowseRequested;

    public MainViewModel(
        Action<Action> uiPost = null,
        Func<List<UsbDeviceIdentity>> deviceProvider = null,
        Func<IEnumerable<EdlJob>, IReadOnlyList<EdlJobResult>> edlRunner = null,
        Func<string> fastbootLocator = null,
        Func<List<(string Serial, string Description)>> fastbootLister = null)
    {
        _uiPost = uiPost ?? (action => action());
        _deviceProvider = deviceProvider ?? ListUsbDevices;
        _edlRunner = edlRunner ?? (jobs => new MultiDeviceFlasher().Run(jobs));
        _fastbootLocator = fastbootLocator ?? FastbootCli.LocateFastboot;
        _fastbootLister = fastbootLister ?? (() =>
        {
            string path = FastbootCli.LocateFastboot();
            return path == null ? new List<(string, string)>() : FastbootCli.ListDevices(path);
        });

        RefreshCommand = new RelayCommand(_ => Refresh(), _ => !IsBusy);
        BrowseCommand = new RelayCommand(_ => BrowseRequested?.Invoke());
        FlashEdlCommand = new RelayCommand(_ => RunEdl(EdlOperation.Flash, destructive: true), _ => CanRun);
        FlashFastbootAllCommand = new RelayCommand(_ => RunFastboot(FlashScript.Variant.CleanAll), _ => CanRun);
        FlashFastbootExceptStorageCommand = new RelayCommand(_ => RunFastboot(FlashScript.Variant.ExceptStorage), _ => CanRun);
        BackupCommand = new RelayCommand(_ => RunEdl(EdlOperation.Backup, destructive: false), _ => CanRun);
        RebootCommand = new RelayCommand(_ => RunEdl(EdlOperation.Reboot, destructive: false), _ => CanRun);
        DoctorCommand = new RelayCommand(_ => RunDoctor(), _ => !IsBusy);

        _reporter.Received += status => _uiPost(() => ApplyStatus(status));
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand FlashEdlCommand { get; }
    public RelayCommand FlashFastbootAllCommand { get; }
    public RelayCommand FlashFastbootExceptStorageCommand { get; }
    public RelayCommand BackupCommand { get; }
    public RelayCommand RebootCommand { get; }
    public RelayCommand DoctorCommand { get; }

    public DeviceViewModel SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value, nameof(SelectedDevice));
    }

    public string RomDirectory
    {
        get => _romDirectory;
        set => SetProperty(ref _romDirectory, value ?? string.Empty, nameof(RomDirectory));
    }

    /// <summary>0 auto · 1 lite · 2 ddr4 · 3 ddr5 (miroir de l'option CLI --programmer).</summary>
    public int ProgrammerIndex
    {
        get => _programmerIndex;
        set => SetProperty(ref _programmerIndex, value, nameof(ProgrammerIndex));
    }

    /// <summary>Équivalent GUI de la confirmation « yes » du CLI (M4.4).</summary>
    public bool Confirmed
    {
        get => _confirmed;
        set
        {
            if (SetProperty(ref _confirmed, value, nameof(Confirmed)))
            {
                RaiseCommands();
            }
        }
    }

    /// <summary>Surveillance hotplug (UsbDeviceWatcher, M2.4).</summary>
    public bool Watch
    {
        get => _watch;
        set
        {
            if (SetProperty(ref _watch, value, nameof(Watch)))
            {
                ToggleWatcher(value);
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value, nameof(IsBusy)))
            {
                RaiseCommands();
            }
        }
    }

    private bool CanRun => !IsBusy;

    /// <summary>Branché au démarrage de la fenêtre (App) : sink + reporter + 1er scan.</summary>
    public void Initialize()
    {
        // Journal : fichier de session (XDG, portage CLI M4.5) + vue GUI.
        Log.Sink = (device, message) =>
        {
            SessionLog.Write(device, message);
            AddLog(message);
        };
        FlashStatus.Reporter = _reporter;
        Refresh();
    }

    /// <summary>Re-scanne les appareils (EDL 9008 + fastboot libusb).</summary>
    public void Refresh()
    {
        List<UsbDeviceIdentity> devices;
        try
        {
            devices = _deviceProvider() ?? new List<UsbDeviceIdentity>();
        }
        catch (Exception ex)
        {
            AddLog("énumération USB impossible : " + ex.Message);
            return;
        }

        List<DeviceViewModel> stale = Devices.ToList();
        Devices.Clear();
        _deviceIndex.Clear();
        foreach (UsbDeviceIdentity device in devices)
        {
            var vm = new DeviceViewModel(Label(device), device.Kind == UsbDeviceKind.Edl9008, device.Location, device.SerialNumber);
            Devices.Add(vm);
            _deviceIndex[vm.DisplayName] = vm;
        }

        // Conservation de la sélection et de l'état si l'appareil est toujours là.
        foreach (DeviceViewModel vm in Devices)
        {
            DeviceViewModel old = stale.FirstOrDefault(d => d.DisplayName == vm.DisplayName);
            if (old != null)
            {
                vm.Progress = old.Progress;
                vm.StatusText = old.StatusText;
                vm.IsRunning = old.IsRunning;
            }
        }
        if (SelectedDevice != null && Devices.Any(d => d.DisplayName == SelectedDevice.DisplayName))
        {
            SelectedDevice = Devices.First(d => d.DisplayName == SelectedDevice.DisplayName);
        }
        else
        {
            SelectedDevice = Devices.FirstOrDefault();
        }

        int edl = Devices.Count(d => d.IsEdl);
        int fb = Devices.Count - edl;
        AddLog($"scan : {edl} appareil(s) EDL 9008, {fb} fastboot");
    }

    /// <summary>Ajoute une ligne horodatée au journal (thread-safe, marshalisée).</summary>
    public void AddLog(string line)
    {
        _uiPost(() =>
        {
            LogLines.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line);
            while (LogLines.Count > MaxLogLines)
            {
                LogLines.RemoveAt(0);
            }
        });
    }

    // ------------------------------------------------------------ EDL ----

    /// <summary>Lance une opération EDL sur la sélection (ou tous les 9008).</summary>
    public void RunEdl(EdlOperation operation, bool destructive)
    {
        if (IsBusy)
        {
            return;
        }
        // Toutes les opérations EDL ouvrent une session firehose : le
        // répertoire doit contenir le programmer (reboot compris — cf.
        // RebootCommand, défaut « répertoire courant » côté CLI).
        if (!Directory.Exists(RomDirectory))
        {
            AddLog("répertoire ROM invalide : " + RomDirectory);
            return;
        }
        if (destructive && !Confirmed)
        {
            AddLog("opération destructive : cochez la case de confirmation (équivalent --yes)");
            return;
        }

        List<DeviceViewModel> targets = EdlTargets();
        if (targets.Count == 0)
        {
            AddLog("aucun appareil EDL 9008 détecté — voir doctor / guide (adb reboot edl, Vol- + câble)");
            return;
        }

        string capturedDir = RomDirectory;
        int capturedProgrammer = ProgrammerIndex;
        var jobs = new List<EdlJob>();
        foreach (DeviceViewModel target in targets)
        {
            DeviceViewModel captured = target;
            target.Reset();
            jobs.Add(new EdlJob
            {
                DeviceName = captured.DisplayName,
                SwPath = capturedDir,
                Operation = operation,
                TransportFactory = () => OpenTransport(captured),
                Configure = flasher =>
                {
                    flasher.programmerName = capturedProgrammer;
                    flasher.verbose = true;
                },
            });
        }

        AddLog($"EDL {operation} → {jobs.Count} appareil(s) ({capturedDir}, programmer {ProgrammerLabel(capturedProgrammer)})");
        RunBackground(jobs, targets);
    }

    // -------------------------------------------------------- fastboot ----

    /// <summary>Lance un flash fastboot (moteur natif M5) sur la sélection.</summary>
    public void RunFastboot(FlashScript.Variant variant)
    {
        if (IsBusy)
        {
            return;
        }
        if (!Directory.Exists(RomDirectory))
        {
            AddLog("répertoire ROM invalide : " + RomDirectory);
            return;
        }
        if (!Confirmed)
        {
            AddLog("opération destructive : cochez la case de confirmation (équivalent --yes)");
            return;
        }
        string fastbootPath = _fastbootLocator();
        if (fastbootPath == null)
        {
            AddLog("fastboot introuvable sur le PATH — installez 'fastboot'/'android-tools' (voir doctor)");
            return;
        }

        List<(string Serial, string Description)> fastbootDevices;
        try
        {
            fastbootDevices = _fastbootLister();
        }
        catch (Exception ex)
        {
            AddLog("impossible de lister les appareils fastboot : " + ex.Message);
            return;
        }
        if (fastbootDevices.Count == 0)
        {
            AddLog("aucun appareil fastboot détecté");
            return;
        }

        // Sélection : fastboot choisi dans la liste, sinon tous.
        List<string> serials;
        if (SelectedDevice is { IsEdl: false } && fastbootDevices.Any(d => d.Serial == SelectedDevice.SerialNumber))
        {
            serials = new List<string> { SelectedDevice.SerialNumber };
        }
        else
        {
            serials = fastbootDevices.Select(d => d.Serial).ToList();
        }

        string scriptPath = FlashScript.Resolve(RomDirectory, variant, null);
        if (scriptPath == null)
        {
            AddLog($"script flash_all introuvable dans {RomDirectory} (variante {variant})");
            return;
        }

        var targets = new List<DeviceViewModel>();
        foreach (string serial in serials)
        {
            // Cibles fastboot volatiles (listées par fastboot, pas libusb) :
            // remplacent toute entrée de même serial pour la progression.
            DeviceViewModel previous = _deviceIndex.TryGetValue(serial, out DeviceViewModel old)
                ? old
                : null;
            if (previous != null)
            {
                _deviceIndex.Remove(serial);
                Devices.Remove(previous);
            }
            DeviceViewModel fresh = new(serial, isEdl: false, location: null, serialNumber: serial);
            Devices.Add(fresh);
            _deviceIndex[serial] = fresh;
            targets.Add(fresh);
        }

        IsBusy = true;
        string capturedScript = scriptPath;
        string capturedFastboot = fastbootPath;
        List<DeviceViewModel> capturedTargets = targets;
        AddLog($"fastboot {variant} → {serials.Count} appareil(s), script {Path.GetFileName(capturedScript)}");
        foreach (DeviceViewModel target in capturedTargets)
        {
            target.Reset();
        }

        _ = Task.Run(() =>
        {
            var results = new List<(string Serial, bool Success, string Message)>();
            var tasks = capturedTargets.Select(target => Task.Run(() =>
            {
                var flasher = new FastbootFlasher
                {
                    FastbootPath = capturedFastboot,
                    DeviceName = target.SerialNumber,
                    DeviceOnline = s => _fastbootLister().Any(d => d.Serial == s),
                };
                try
                {
                    flasher.Flash(capturedScript);
                    return (target.SerialNumber, true, "flash done");
                }
                catch (Exception ex)
                {
                    return (target.SerialNumber, false, ex.Message);
                }
            })).ToArray();
            Task.WaitAll(tasks);
            results.AddRange(tasks.Select(t => t.Result));
            _uiPost(() =>
            {
                foreach (var (serial, success, message) in results)
                {
                    AddLog(success
                        ? $"{serial}: succès — {message}"
                        : $"{serial}: ÉCHEC — {message}");
                    SessionLog.Complete(serial, success, message);
                }
                IsBusy = false;
            });
        });
    }

    // ---------------------------------------------------------- doctor ----

    private void RunDoctor()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        _ = Task.Run(() =>
        {
            List<DoctorService.Check> checks;
            try
            {
                checks = DoctorService.Run();
            }
            catch (Exception ex)
            {
                checks = new List<DoctorService.Check> { new("runtime", DoctorService.CheckStatus.Fail, ex.Message) };
            }
            _uiPost(() =>
            {
                foreach (DoctorService.Check check in checks)
                {
                    AddLog($"doctor [{check.Status}] {check.Name}: {check.Detail}");
                }
                IsBusy = false;
            });
        });
    }

    // ------------------------------------------------------- interne ----

    private List<DeviceViewModel> EdlTargets()
    {
        if (SelectedDevice is { IsEdl: true })
        {
            return new List<DeviceViewModel> { SelectedDevice };
        }
        return Devices.Where(d => d.IsEdl).ToList();
    }

    private void RunBackground(List<EdlJob> jobs, List<DeviceViewModel> targets)
    {
        IsBusy = true;
        _ = Task.Run(() =>
        {
            IReadOnlyList<EdlJobResult> results;
            try
            {
                results = _edlRunner(jobs);
            }
            catch (Exception ex)
            {
                results = jobs.Select(j => new EdlJobResult
                {
                    DeviceName = j.DeviceName,
                    Success = false,
                    Message = ex.Message,
                    Error = ex,
                }).ToList();
            }
            _uiPost(() =>
            {
                foreach (EdlJobResult result in results)
                {
                    AddLog(result.Success
                        ? $"{result.DeviceName}: succès — {result.Message}"
                        : $"{result.DeviceName}: ÉCHEC — {result.Message ?? result.Error?.Message}");
                    SessionLog.Complete(result.DeviceName, result.Success, result.Message ?? result.Error?.Message);
                }
                IsBusy = false;
            });
        });
    }

    private void ApplyStatus(GuiReporter.Status status)
    {
        DeviceViewModel device = Devices.FirstOrDefault(d => d.Matches(status.DeviceName));
        if (device == null)
        {
            return;
        }
        if (status.Progress.HasValue)
        {
            device.Progress = status.Progress.Value;
        }
        if (!string.IsNullOrEmpty(status.StatusText))
        {
            device.StatusText = status.StatusText;
        }
        if (status.IsDone)
        {
            device.IsRunning = false;
            device.Progress = 1;
            if (status.Result == "error")
            {
                device.StatusText = "échec : " + status.StatusText;
            }
        }
    }

    private static string Label(UsbDeviceIdentity device)
    {
        string kind = device.Kind == UsbDeviceKind.Edl9008 ? "9008" : "fastboot";
        return kind + "@" + device.Location
            + (string.IsNullOrEmpty(device.SerialNumber) ? "" : "#" + device.SerialNumber);
    }

    private static string ProgrammerLabel(int index)
    {
        return index switch
        {
            1 => "lite",
            2 => "ddr4",
            3 => "ddr5",
            _ => "auto",
        };
    }

    private void RaiseCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        FlashEdlCommand.RaiseCanExecuteChanged();
        FlashFastbootAllCommand.RaiseCanExecuteChanged();
        FlashFastbootExceptStorageCommand.RaiseCanExecuteChanged();
        BackupCommand.RaiseCanExecuteChanged();
        RebootCommand.RaiseCanExecuteChanged();
        DoctorCommand.RaiseCanExecuteChanged();
    }

    private void ToggleWatcher(bool enable)
    {
        if (enable)
        {
            if (_watcher == null)
            {
                try
                {
                    _watcher = new UsbDeviceWatcher
                    {
                        Interval = TimeSpan.FromSeconds(2),
                    };
                    _watcher.DeviceArrived += _ => _uiPost(Refresh);
                    _watcher.DeviceLeft += _ => _uiPost(Refresh);
                }
                catch (Exception ex)
                {
                    AddLog("surveillance impossible : " + ex.Message);
                    _watch = false;
                    OnPropertyChanged(nameof(Watch));
                    return;
                }
            }
            _watcher.Start();
            AddLog("surveillance hotplug activée");
        }
        else
        {
            _watcher?.Stop();
            if (_watcher != null)
            {
                AddLog("surveillance hotplug désactivée");
            }
        }
    }

    private static List<UsbDeviceIdentity> ListUsbDevices()
    {
        using var enumerator = new UsbDeviceEnumerator();
        return enumerator.ListDevices();
    }

    /// <summary>Fabrique de transport USB pour un 9008 — contexte libusb
    /// frais par ouverture, non disposé en cas de succès (le transport en
    /// est propriétaire, cf. EdlTargets.OpenByIdentity côté CLI).</summary>
    private static ITransport OpenTransport(DeviceViewModel device)
    {
        var enumerator = new UsbDeviceEnumerator();
        try
        {
            UsbDeviceIdentity identity = enumerator.ListDevices()
                .FirstOrDefault(d => d.Kind == UsbDeviceKind.Edl9008
                    && string.Equals(d.Location, device.Location, StringComparison.OrdinalIgnoreCase));
            if (identity == null)
            {
                throw new InvalidOperationException("l'appareil " + device.DisplayName + " n'est plus détecté");
            }
            return enumerator.OpenEdlDevice(identity);
        }
        catch
        {
            enumerator.Dispose();
            throw;
        }
    }
}
