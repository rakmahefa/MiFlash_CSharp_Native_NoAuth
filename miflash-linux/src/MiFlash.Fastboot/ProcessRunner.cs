using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using MiFlash.Core.Runtime;

namespace MiFlash.Fastboot;

/// <summary>
/// Exécuteur de processus POSIX (M5.1) — port de <c>XiaoMiFlash.code.Utility.Cmd</c>
/// sans cmd.exe : sortie ligne à ligne (stdout+stderr) avec détection d'erreur
/// fidèle (<c>error</c>/<c>fail</c>/<c>missmatching image and device</c>),
/// timeout par commande, kill de l'arborescence (<c>Kill(entireProcessTree)</c>
/// remplace <c>taskkill</c>), statuts <see cref="FlashStatus"/> et journal
/// <see cref="Log"/>. Écarts : pas de <c>pushd/popd</c> (working directory du
/// processus), pas de gestion COM MTK (usine).
/// </summary>
public sealed class ProcessRunner
{
    /// <summary>Délai par défaut d'un appel <c>fastboot devices</c>.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly string _deviceName;

    /// <summary>Premier message d'erreur détecté (port de <c>Cmd.errMsg</c>).</summary>
    public string ErrMsg = "";

    public ProcessRunner(string deviceName)
    {
        _deviceName = deviceName ?? "process";
    }

    /// <summary>
    /// Exécute <paramref name="fileName"/> avec les arguments donnés, diffuse
    /// la sortie ligne à ligne (Log + FlashStatus), applique le timeout et
    /// renvoie la sortie agrégée. Retourne le code de sortie du processus.
    /// </summary>
    /// <param name="fileName">Exécutable (ex. fastboot résolu sur PATH).</param>
    /// <param name="arguments">Arguments (déjà quotés si besoin).</param>
    /// <param name="workingDirectory">Répertoire de travail (remplace pushd).</param>
    /// <param name="timeout">Timeout (null = illimité, comme Execute(seconds=0)).</param>
    /// <param name="percent">Progression 0–1 à rapporter avec chaque ligne.</param>
    /// <param name="cancellationToken">Annulation (kill l'arborescence).</param>
    public int Execute(
        string fileName,
        string arguments,
        string workingDirectory = null,
        TimeSpan? timeout = null,
        float? percent = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return 0;
        }
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        var output = new StringBuilder();
        try
        {
            process.Start();
            Log.w(_deviceName, "start process id " + process.Id + " name " + process.ProcessName);

            // Lecture ligne à ligne des deux flux (process_OutputDataReceived /
            // process_ErrorDataReceived l.271–316) : Log + statut + errMsg.
            var stdoutDone = new ManualResetEventSlim(false);
            var stderrDone = new ManualResetEventSlim(false);
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                    HandleLine(e.Data, percent);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                    HandleLine(e.Data, percent);
                }
                else
                {
                    stderrDone.Set();
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool timedOut = false;
            if (timeout.HasValue)
            {
                timedOut = !WaitForExit(process, timeout.Value, cancellationToken);
            }
            else
            {
                WaitForExitUninterruptible(process, cancellationToken);
            }
            if (timedOut || cancellationToken.IsCancellationRequested)
            {
                KillTree(process);
                Log.w(_deviceName, "process killed (timeout or cancel), pid " + process.Id);
                if (timedOut && string.IsNullOrEmpty(ErrMsg))
                {
                    ErrMsg = "process timeout";
                }
                process.WaitForExit(5000);
                return -1;
            }
            process.WaitForExit(); // attendre la fin des handlers async de sortie
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.w(_deviceName, ex.Message);
            if (string.IsNullOrEmpty(ErrMsg))
            {
                ErrMsg = ex.Message;
            }
            return -1;
        }
        finally
        {
            Log.w(_deviceName, "process exit.");
        }
    }

    private void HandleLine(string data, float? percent)
    {
        Log.w(_deviceName, data);
        FlashStatus.UpdateDeviceStatus(_deviceName, percent, data, "flashing", isDone: false);
        string lower = (data ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrEmpty(ErrMsg)
            && (lower.Contains("error") || lower.Contains("fail")
                || lower == "missmatching image and device"
                || lower == "missmatching board version"))
        {
            ErrMsg = data;
        }
    }

    /// <summary>Kill de l'arborescence (remplace taskkill /T /F de killCmdProcess).</summary>
    public static void KillTree(Process process)
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // processus déjà mort : rien à faire
        }
    }

    /// <summary>Attente avec timeout + annulation (WaitForExit(ms) en boucle :
    /// pas de surcharge CancellationToken en .NET 8).</summary>
    private static bool WaitForExit(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }
            process.WaitForExit(200);
        }
        return true;
    }

    private static void WaitForExitUninterruptible(Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                WaitForExit(process, TimeSpan.FromMilliseconds(int.MaxValue), cancellationToken);
            }
            else
            {
                process.WaitForExit();
            }
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
        }
    }
}
