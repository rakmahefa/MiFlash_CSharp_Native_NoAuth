using System;
using System.IO;
using System.Linq;
using MiFlash.Cli.Io;
using Xunit;

namespace MiFlash.Cli.Tests;

/// <summary>
/// Tests de la journalisation de session (M4.5) : fichier par appareil,
/// déplacement pass|fail (port de moveLog) et journal résultat global.
/// </summary>
public class SessionLogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _oldLogDir;

    public SessionLogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "miflash-slog-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _oldLogDir = SessionLog.LogDirectory;
        SessionLog.LogDirectory = _tempDir;
    }

    public void Dispose()
    {
        SessionLog.LogDirectory = _oldLogDir;
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Write_creates_per_device_file_with_message()
    {
        SessionLog.Write("9008@1.3", "hello from device");

        string file = Directory.EnumerateFiles(_tempDir, "9008@1.3@*.txt").Single();
        string content = File.ReadAllText(file);
        Assert.Contains("hello from device", content);
    }

    [Fact]
    public void Complete_moves_file_to_pass_and_logs_result()
    {
        SessionLog.Write("9008@1.3", "flashing boot");
        string passDir = Path.Combine(_tempDir, DateTime.Now.ToString("yyyyMd"), "pass");

        string finalPath = SessionLog.Complete("9008@1.3", success: true, "flash done");

        Assert.NotNull(finalPath);
        Assert.StartsWith(passDir, finalPath);
        Assert.True(File.Exists(finalPath), "le fichier doit exister dans pass/");
        Assert.Contains("flash done", File.ReadAllText(finalPath));
        Assert.False(Directory.EnumerateFiles(_tempDir, "9008@1.3@*.txt").Any(),
            "le fichier d'origine doit avoir été déplacé");
        string resultLog = Path.Combine(_tempDir, "result.log");
        Assert.True(File.Exists(resultLog));
        Assert.Contains("PASS | 9008@1.3 | flash done", File.ReadAllText(resultLog));
    }

    [Fact]
    public void Complete_failure_moves_file_to_fail()
    {
        SessionLog.Write("9008@2.1", "boom");

        string finalPath = SessionLog.Complete("9008@2.1", success: false, "configure failed");

        Assert.Contains(Path.Combine(DateTime.Now.ToString("yyyyMd"), "fail"), finalPath);
        Assert.True(File.Exists(finalPath));
        string resultLog = Path.Combine(_tempDir, "result.log");
        Assert.Contains("FAIL | 9008@2.1 | configure failed", File.ReadAllText(resultLog));
    }

    [Fact]
    public void Complete_without_session_returns_null()
    {
        Assert.Null(SessionLog.Complete("never-seen", success: true));
    }

    [Fact]
    public void Sanitize_replaces_path_separators()
    {
        Assert.Equal("9008_1.3", SessionLog.Sanitize("9008/1.3"));
        Assert.Equal("ttyUSB0", SessionLog.Sanitize("ttyUSB0"));
        // Les séparateurs deviennent '_' : nom de fichier valide.
        Assert.Equal("___", SessionLog.Sanitize("///"));
        Assert.Equal("device", SessionLog.Sanitize(""));
    }
}
