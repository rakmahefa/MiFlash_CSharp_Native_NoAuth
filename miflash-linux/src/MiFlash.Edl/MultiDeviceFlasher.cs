using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MiFlash.Core.Runtime;
using MiFlash.Core.Transport;

namespace MiFlash.Edl;

/// <summary>Travail de flash soumis au <see cref="MultiDeviceFlasher"/> :
/// un appareil (nom logique), un répertoire d'images et une fabrique de
/// transport (le port est rouvert à chaque essai, cf. reset_to_edl).</summary>
public sealed class EdlJob
{
    public string DeviceName { get; init; }

    public string SwPath { get; init; }

    /// <summary>Fabrique le transport pour cet appareil (appelée au lancement ;
    /// le <see cref="EdlFlasher"/> gère ensuite Close/Open sur le même objet).</summary>
    public Func<ITransport> TransportFactory { get; init; }

    /// <summary>Personnalisation optionnelle du flasher avant exécution
    /// (programmerName, authenticator, verbose…).</summary>
    public Action<EdlFlasher> Configure { get; init; }
}

/// <summary>Résultat d'un travail EDL — succès, message, erreur éventuelle.</summary>
public sealed class EdlJobResult
{
    public string DeviceName { get; init; }

    public bool Success { get; init; }

    public string Message { get; init; }

    public Exception Error { get; init; }

    public override string ToString()
    {
        return DeviceName + ": " + (Success ? "success" : "error") + (Message is null ? "" : " — " + Message);
    }
}

/// <summary>
/// Orchestrateur multi-appareils (M3.9) — équivalent du pool de threads de
/// <c>DeviceCtrl</c>/<c>FlashingDevice</c> : une <see cref="Task"/> par
/// appareil, progression par appareil via <see cref="FlashStatus"/>, arrêt
/// propre par <see cref="CancellationToken"/> (les tâches en cours finissent
/// leur pas courant puis remontent <see cref="OperationCanceledException"/>).
/// </summary>
public sealed class MultiDeviceFlasher
{
    /// <summary>Appareils flaschés simultanément (défaut 4, comme la file
    /// d'attente matérielle de l'atelier d'origine).</summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>Lance les travaux et attend leur terminaison. Les résultats
    /// sont retournés dans l'ordre de soumission ; une tâche en échec n'arrête
    /// pas les autres (comme l'original, qui journalise par appareil).</summary>
    public IReadOnlyList<EdlJobResult> Run(IEnumerable<EdlJob> jobs, CancellationToken cancellationToken = default)
    {
        if (jobs is null)
        {
            throw new ArgumentNullException(nameof(jobs));
        }
        var jobList = jobs.ToList();
        var results = new EdlJobResult[jobList.Count];
        using var throttle = new SemaphoreSlim(Math.Max(1, MaxDegreeOfParallelism));
        var tasks = new Task[jobList.Count];
        for (int i = 0; i < jobList.Count; i++)
        {
            int index = i;
            throttle.Wait(cancellationToken);
            tasks[index] = Task.Run(() =>
            {
                try
                {
                    results[index] = RunJob(jobList[index], cancellationToken);
                }
                finally
                {
                    throttle.Release();
                }
            }, cancellationToken);
        }
        Task.WaitAll(tasks, cancellationToken);
        return results;
    }

    private static EdlJobResult RunJob(EdlJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flasher = new EdlFlasher(job.DeviceName, job.SwPath, job.TransportFactory());
        job.Configure?.Invoke(flasher);
        try
        {
            flasher.Flash();
            return new EdlJobResult { DeviceName = job.DeviceName, Success = true, Message = "flash done" };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // L'original journalise l'erreur par appareil et continue les autres.
            return new EdlJobResult { DeviceName = job.DeviceName, Success = false, Message = ex.Message, Error = ex };
        }
    }
}
