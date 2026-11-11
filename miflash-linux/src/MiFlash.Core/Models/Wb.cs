// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : Wb.cs
namespace MiFlash.Core.Models;

/// <summary>Modèle WriteBooster du provision.xml UFS.</summary>
public class Wb
{
    public string Vendor { get; set; }
    public string Partnumber { get; set; }
    public string Wb_size_in_MB { get; set; }
    public string Hpb_total_range_in_MB { get; set; }
}
