using System.IO;
using MiFlash.Core.Io;
using Xunit;

namespace MiFlash.Core.Tests;

// Mutation des variables XDG du process → collection sérialisée avec
// LanguageProviderTests (cf. EnvSerialCollection).
[Collection("env-serial")]
public class AppPathsTests
{
    [Fact]
    public void Xdg_overrides_are_honored()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "miflash-xdg-" + Path.GetRandomFileName());
        try
        {
            string data = Path.Combine(tmp, "data");
            string state = Path.Combine(tmp, "state");
            string config = Path.Combine(tmp, "config");
            System.Environment.SetEnvironmentVariable("XDG_DATA_HOME", data);
            System.Environment.SetEnvironmentVariable("XDG_STATE_HOME", state);
            System.Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);

            Assert.Equal(data + "/miflash", AppPaths.DataRoot);
            Assert.Equal(Path.Combine(state, "miflash"), AppPaths.StateRoot);
            Assert.Equal(Path.Combine(state, "miflash", "log"), AppPaths.LogDir);
            Assert.Equal(Path.Combine(config, "miflash", "config.json"), AppPaths.ConfigFile);

            AppPaths.EnsureDirectories();
            Assert.True(Directory.Exists(AppPaths.LogDir));
            Assert.True(Directory.Exists(Path.GetDirectoryName(AppPaths.ConfigFile)));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            System.Environment.SetEnvironmentVariable("XDG_STATE_HOME", null);
            System.Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            try
            {
                Directory.Delete(tmp, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Defaults_follow_xdg_spec()
    {
        // Sans variable XDG : ~/.local/{share,state}/miflash et ~/.config/miflash/config.json.
        System.Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        System.Environment.SetEnvironmentVariable("XDG_STATE_HOME", null);
        System.Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);

        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".local", "share", "miflash"), AppPaths.DataRoot);
        Assert.Equal(Path.Combine(home, ".local", "state", "miflash"), AppPaths.StateRoot);
        Assert.Equal(Path.Combine(home, ".local", "state", "miflash", "log"), AppPaths.LogDir);
        Assert.Equal(Path.Combine(home, ".config", "miflash", "config.json"), AppPaths.ConfigFile);
    }
}
