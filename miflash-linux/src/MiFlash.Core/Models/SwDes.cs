// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : SwDes.cs
namespace MiFlash.Core.Models;

/// <summary>Description d'un logiciel téléchargé (usine — conservé pour compat).</summary>
public class SwDes
{
    public double fileSize;
    public string serverPath;
    public double percent;
    public string localPath;
    public bool isDone;
}
