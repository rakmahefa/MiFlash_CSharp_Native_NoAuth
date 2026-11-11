// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : CheckCPUIDResult.cs
namespace MiFlash.Core.Models;

/// <summary>Résultat de la vérification CPUID (choix du programmer DDR4/DDR5).</summary>
public class CheckCPUIDResult
{
    public string Device { get; set; }
    public bool Result { get; set; }
    public string Path { get; set; }
    public string Msg { get; set; }
}
