using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MiFlash.Core.Runtime;

namespace MiFlash.Fastboot;

/// <summary>
/// Exécution d'un script flash_all sur un appareil fastboot (M5.4/M5.5) —
/// port de <c>ScriptDevice.flash()</c>/<c>fastbootflash()</c> :
/// <list type="bullet">
/// <item>timeout global 11 min 40 s (TimeOut.DoWithTimeout) : kill de
/// l'arborescence et statut « flash timeout » ;</item>
/// <item>cas Intel DNX (8087:0a65) : <c>fastboot boot loader.efi</c>
/// (kernelflinger) avant le script, avec attente de ré-énumération ;</item>
/// <item>chaque opération est exécutée avec <c>-s serial</c> ; code de sortie
/// non nul ou ligne « error/fail » → échec (sémantique <c>|| exit /B 1</c>) ;</item>
/// <item>progression = opérations terminées / total (remplace DummyProgress).</item>
/// </list>
/// Écarts v1 : pas de MES/X5MES (usine, hors périmètre), pas de GrapLog
/// (lkmsg/lpmsg, usine).
/// </summary>
public sealed class FastbootFlasher
{
    /// <summary>Timeout global du flash (11 min 40 s, cf. ScriptDevice.flash).</summary>
    public static readonly TimeSpan GlobalFlashTimeout = new TimeSpan(0, 0, 11, 40);

    /// <summary>Chemin du binaire fastboot (résolu par FastbootCli.LocateFastboot).</summary>
    public string FastbootPath { get; set; }

    /// <summary>Numéro de série fastboot de la cible (-s).</summary>
    public string DeviceName { get; set; }

    /// <summary>VID/PID USB de la cible (détection Intel DNX 8087:0a65).</summary>
    public ushort UsbVendorId { get; set; }

    public ushort UsbProductId { get; set; }

    /// <summary>Predicate indiquant que la cible fastboot est (re)connectée
    /// (ré-énumération après kernelflinger) ; null → attendre bêtement.</summary>
    public Func<string, bool> DeviceOnline { get; set; }

    public void Flash(string scriptPath)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Cas Intel DNX : boot kernelflinger avant le script (l.205–238).
            if (UsbVendorId == 0x8087 && UsbProductId == 0x0a65)
            {
                Log.w(DeviceName, "DNX fastboot mode");
                FlashStatus.UpdateDeviceStatus(DeviceName, null, "DNX fastboot mode", "boot into kerneflinger", isDone: false);
                BootKernelflinger(scriptPath, stopwatch);
            }

            RunScript(scriptPath, stopwatch);
            FlashStatus.UpdateDeviceStatus(DeviceName, 1f, "flash done", "success", isDone: true);
        }
        catch (Exception ex)
        {
            FlashStatus.UpdateDeviceStatus(DeviceName, null, ex.Message, "error", isDone: true);
            throw;
        }
    }

    /// <summary>fastboot boot &lt;loader.efi&gt; puis attente de ré-énumération (≤5 s).</summary>
    private void BootKernelflinger(string scriptPath, Stopwatch stopwatch)
    {
        string loader = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(scriptPath)), "images", "loader.efi");
        if (!File.Exists(loader))
        {
            Log.w(DeviceName, "error:couldn't find " + loader);
            return;
        }
        Log.w(DeviceName, "Boot into kerneflinger " + loader);
        var runner = new ProcessRunner(DeviceName);
        int code = runner.Execute(FastbootPath, $"-s {DeviceName} boot \"{loader}\"",
            timeout: Remaining(stopwatch));
        if (code != 0)
        {
            throw new Exception("kernelflinger boot failed: " + (runner.ErrMsg ?? "exit " + code));
        }
        // Attente de ré-énumération fastboot (l.219–229 : 4 essais × 1 s).
        for (int i = 0; i < 5 && DeviceOnline != null && !DeviceOnline(DeviceName); i++)
        {
            Thread.Sleep(1000);
        }
    }

    private void RunScript(string scriptPath, Stopwatch stopwatch)
    {
        Log.w(DeviceName, "script : " + scriptPath);
        var ops = ScriptEngine.Parse(scriptPath);
        if (ops.Count == 0)
        {
            throw new Exception("no fastboot operation found in " + Path.GetFileName(scriptPath));
        }
        var runner = new ProcessRunner(DeviceName);
        string romDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
        for (int i = 0; i < ops.Count; i++)
        {
            CheckTimeout(stopwatch);
            FastbootOp op = ops[i];
            float percent = (float)i / ops.Count;
            if (op.Verb == "sleep")
            {
                Log.w(DeviceName, "wait " + op.SleepSeconds + "s");
                Thread.Sleep(TimeSpan.FromSeconds(Math.Min(op.SleepSeconds, Remaining(stopwatch).TotalSeconds)));
                continue;
            }
            Log.w(DeviceName, "fastboot " + op.Verb + " " + op.Args);
            int code = runner.Execute(
                FastbootPath,
                BuildArguments(op),
                workingDirectory: romDir,
                timeout: Remaining(stopwatch),
                percent: percent);
            if (code != 0 || !string.IsNullOrEmpty(runner.ErrMsg))
            {
                throw new Exception("fastboot " + op.Verb + " failed: "
                    + (string.IsNullOrEmpty(runner.ErrMsg) ? "exit " + code : runner.ErrMsg));
            }
        }
    }

    /// <summary>-s serial + verbe + arguments (chemins quotés si espaces).</summary>
    private string BuildArguments(FastbootOp op)
    {
        var sb = new System.Text.StringBuilder("-s ").Append(DeviceName);
        sb.Append(' ').Append(op.Verb);
        foreach (string word in op.Args.Words)
        {
            sb.Append(' ');
            if (word.Contains(' ') && !word.StartsWith('"'))
            {
                sb.Append('"').Append(word).Append('"');
            }
            else
            {
                sb.Append(word);
            }
        }
        return sb.ToString();
    }

    private static void CheckTimeout(Stopwatch stopwatch)
    {
        if (stopwatch.Elapsed >= GlobalFlashTimeout)
        {
            throw new Exception("flash timeout");
        }
    }

    private static TimeSpan Remaining(Stopwatch stopwatch)
    {
        TimeSpan remaining = GlobalFlashTimeout - stopwatch.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
