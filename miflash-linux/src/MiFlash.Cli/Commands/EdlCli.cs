using System;
using System.CommandLine;
using System.Collections.Generic;
using System.IO;
using MiFlash.Core.Firehose;

namespace MiFlash.Cli.Commands;

/// <summary>
/// Options communes aux commandes EDL (M4.3) et leur lecture —
/// <c>&lt;dir&gt; [--programmer auto|lite|ddr4|ddr5] [--device id]… [--serial port]
/// [--read-dump] [--write-dump] [--backup-dir dir] [--quiet] [--yes]</c>.
/// (API System.CommandLine 2.0.0 GA.)
/// </summary>
internal static class EdlCli
{
    public static Argument<DirectoryInfo> DirectoryArg(string description)
    {
        return new Argument<DirectoryInfo>("dir")
        {
            Description = description,
        };
    }

    public static Option<List<string>> DeviceOption()
    {
        return new Option<List<string>>("--device", new[] { "-d" })
        {
            Description = "target device (location '1.3', 'bus.port' or serial; repeatable; default: all EDL 9008)",
            AllowMultipleArgumentsPerToken = true,
        };
    }

    public static Option<string> SerialOption()
    {
        return new Option<string>("--serial", new[] { "-s" })
        {
            Description = "force serial transport (qcserial /dev/ttyUSB*, fallback M2.5) instead of USB",
        };
    }

    public static Option<string> ProgrammerOption()
    {
        return new Option<string>("--programmer", new[] { "-p" })
        {
            Description = "programmer selection: auto (default), lite, ddr4, ddr5",
            DefaultValueFactory = _ => "auto",
        };
    }

    public static Option<string> BackupDirOption()
    {
        return new Option<string>("--backup-dir")
        {
            Description = "backup root (default: ~/.local/share/miflash)",
        };
    }

    public static Option<bool> ReadDumpOption()
    {
        return new Option<bool>("--read-dump")
        {
            Description = "hex dump of target->host packets (Comm.isReadDump, default on)",
        };
    }

    public static Option<bool> WriteDumpOption()
    {
        return new Option<bool>("--write-dump")
        {
            Description = "hex dump of host->target packets (Comm.isWriteDump)",
        };
    }

    public static Option<bool> QuietOption()
    {
        return new Option<bool>("--quiet", new[] { "-q" })
        {
            Description = "reduce verbose firehose attributes",
        };
    }

    public static Option<bool> YesOption()
    {
        return new Option<bool>("--yes", new[] { "-y" })
        {
            Description = "skip erase/program confirmation",
        };
    }

    /// <summary>Résout le nom de programmer en code FirehoseCommands (0=auto).</summary>
    public static int ParseProgrammer(string value)
    {
        return value switch
        {
            "lite" => FirehoseCommands.PROGRAMMER_LITE,
            "ddr4" => FirehoseCommands.PROGRAMMER_DDR4,
            "ddr5" => FirehoseCommands.PROGRAMMER_DDR5,
            "auto" or null or "" => 0,
            _ => throw new ArgumentException($"unknown programmer '{value}' (auto|lite|ddr4|ddr5)"),
        };
    }

    /// <summary>Lit les options communes depuis le résultat d'analyse.</summary>
    public static EdlRun.Options ReadCommon(
        System.CommandLine.ParseResult parseResult,
        Argument<DirectoryInfo> dir,
        Option<List<string>> device,
        Option<string> serial,
        Option<string> programmer,
        Option<string> backupDir,
        Option<bool> readDump,
        Option<bool> writeDump,
        Option<bool> quiet,
        Option<bool> yes)
    {
        DirectoryInfo directory = parseResult.GetValue(dir);
        string path = directory?.FullName ?? ".";
        if (!System.IO.Directory.Exists(path))
        {
            throw new ArgumentException("directory not found: " + path);
        }
        return new EdlRun.Options
        {
            Directory = path,
            Device = parseResult.GetValue(device) ?? new List<string>(),
            Serial = parseResult.GetValue(serial),
            Programmer = ParseProgrammer(parseResult.GetValue(programmer)),
            BackupDir = parseResult.GetValue(backupDir),
            ReadDump = parseResult.GetValue(readDump),
            WriteDump = parseResult.GetValue(writeDump),
            Quiet = parseResult.GetValue(quiet),
            Yes = parseResult.GetValue(yes),
        };
    }
}
