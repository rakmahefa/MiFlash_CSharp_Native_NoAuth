using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using MiFlash.Core.Io;

namespace MiFlash.Cli.Doctor;

/// <summary>
/// Diagnostics d'environnement (M4.7) — vérifie le runtime .NET, libusb-1.0,
/// les règles udev 51-miflash.rules, l'accès effectif aux appareils 9008,
/// la présence du binaire fastboot et les répertoires XDG. Retourne des
/// vérifications {name, ok, detail} affichables et un code de sortie global.
/// </summary>
public static class DoctorService
{
    public sealed record Check(string Name, CheckStatus Status, string Detail);

    public enum CheckStatus { Ok, Warn, Fail }

    public static List<Check> Run()
    {
        var checks = new List<Check>
        {
            CheckDotnet(),
            CheckLibUsb(),
            CheckUdevRules(),
            CheckFastboot(),
            CheckXdgDirs(),
        };
        return checks;
    }

    private static Check CheckDotnet()
    {
        // Self-contained : le runtime est embarqué dans le répertoire de
        // l'application (System.Private.CoreLib.dll présent à côté de l'exe).
        bool selfContained = File.Exists(Path.Combine(AppContext.BaseDirectory, "System.Private.CoreLib.dll"));
        return new Check(".NET runtime",
            CheckStatus.Ok,
            $"{Environment.Version} ({(selfContained ? "self-contained" : "framework-dependent")}, {Environment.OSVersion})");
    }

    private static Check CheckLibUsb()
    {
        try
        {
            using var context = new UsbContext();
            int count = context.List().Count();
            return new Check("libusb-1.0", CheckStatus.Ok, $"loaded, {count} USB device(s) visible");
        }
        catch (Exception ex)
        {
            return new Check("libusb-1.0", CheckStatus.Fail,
                "cannot load libusb-1.0.so: " + ex.Message + " — install package 'libusb-1.0-0' (deb) / 'libusb1' (rpm)");
        }
    }

    private static Check CheckUdevRules()
    {
        string[] candidates =
        {
            "/etc/udev/rules.d/51-miflash.rules",
            "/usr/lib/udev/rules.d/51-miflash.rules",
            "/lib/udev/rules.d/51-miflash.rules",
        };
        foreach (string path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }
            string content = File.ReadAllText(path);
            if (content.Contains("05c6:9008", StringComparison.Ordinal) || content.Contains("05c6", StringComparison.Ordinal) && content.Contains("9008"))
            {
                return new Check("udev rules", CheckStatus.Ok, path);
            }
            return new Check("udev rules", CheckStatus.Warn,
                path + " exists but does not cover 05c6:9008 — reinstall packaging/udev/51-miflash.rules");
        }
        return new Check("udev rules", CheckStatus.Warn,
            "51-miflash.rules not installed — copy packaging/udev/51-miflash.rules to /etc/udev/rules.d/ then run: sudo udevadm control --reload && sudo udevadm trigger");
    }

    private static Check CheckFastboot()
    {
        string fastboot = FindOnPath("fastboot");
        return fastboot != null
            ? new Check("fastboot binary", CheckStatus.Ok, fastboot)
            : new Check("fastboot binary", CheckStatus.Warn,
                "not found on PATH (M5 scripts need it) — install package 'fastboot' / 'android-tools'");
    }

    private static Check CheckXdgDirs()
    {
        try
        {
            AppPaths.EnsureDirectories();
            return new Check("XDG directories", CheckStatus.Ok,
                $"data: {AppPaths.DataRoot} | state/log: {AppPaths.LogDir}");
        }
        catch (Exception ex)
        {
            return new Check("XDG directories", CheckStatus.Fail, ex.Message);
        }
    }

    /// <summary>Cherche un exécutable sur PATH (équivalent « which »).</summary>
    public static string FindOnPath(string name)
    {
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate) && IsExecutable(new FileInfo(candidate)))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // répertoire illisible : continuer
            }
        }
        return null;
    }

    private static bool IsExecutable(FileInfo info)
    {
        try
        {
            // UnixFileMode (net6+) : xgu|xgo|xa.
            return (info.UnixFileMode & System.IO.UnixFileMode.UserExecute) != 0
                || (info.UnixFileMode & System.IO.UnixFileMode.GroupExecute) != 0
                || (info.UnixFileMode & System.IO.UnixFileMode.OtherExecute) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
