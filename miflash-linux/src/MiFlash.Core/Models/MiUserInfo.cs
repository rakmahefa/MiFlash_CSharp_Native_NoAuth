// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : MiUserInfo.cs
namespace MiFlash.Core.Models;

/// <summary>Infos utilisateur Mi (compte local, hors login Xiaomi v1).</summary>
public class MiUserInfo
{
    public string id = string.Empty;
    public string name = string.Empty;
    public byte[] icon;
}
