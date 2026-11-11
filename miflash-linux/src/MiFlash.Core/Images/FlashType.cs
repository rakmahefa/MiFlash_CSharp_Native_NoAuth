// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : FlashType.cs
namespace MiFlash.Core.Images;

/// <summary>Types de scripts de flash fastboot (M5 traduira .bat ↔ .sh).</summary>
public static class FlashType
{
    public static string CleanAll = "flash_all.bat";
    public static string SaveUserData = "flash_all_except.*\\.bat";
    public static string CleanAllAndLock = "flash_all_lock.bat";
    public static string flash_all_lock_crc = "flash_all_lock_crc.bat";
}
