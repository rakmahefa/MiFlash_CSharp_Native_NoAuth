// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : FlashResult.cs
namespace MiFlash.Core.Models;

/// <summary>Résultat d'une opération de flash (booléen + message).</summary>
public class FlashResult
{
    public bool Result { get; set; }
    public string Msg { get; set; }
}
