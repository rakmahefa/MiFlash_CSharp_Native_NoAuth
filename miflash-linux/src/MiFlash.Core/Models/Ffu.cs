// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : Ffu.cs
namespace MiFlash.Core.Models;

/// <summary>Modèle FFU (image Windows — utilisé par provision).</summary>
public class Ffu
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string Number { get; set; }
    public string File { get; set; }
    public string Size { get; set; }
}
