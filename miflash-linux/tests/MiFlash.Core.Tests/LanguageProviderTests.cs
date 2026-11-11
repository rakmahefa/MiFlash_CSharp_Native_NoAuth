using System;
using System.IO;
using MiFlash.Cli.i18n;
using Xunit;

namespace MiFlash.Cli.Tests;

/// <summary>
/// Tests du portage i18n (M4.6) : bibliothèque embarquée, repli ENG, clé
/// inconnue, détection de locale, surcharge utilisateur XDG.
/// </summary>
public class LanguageProviderTests : IDisposable
{
    private readonly string _oldConfigHome;
    private readonly string _oldLcAll;
    private readonly string _oldLang;

    public LanguageProviderTests()
    {
        _oldConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _oldLcAll = Environment.GetEnvironmentVariable("LC_ALL");
        _oldLang = Environment.GetEnvironmentVariable("LANG");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _oldConfigHome);
        Environment.SetEnvironmentVariable("LC_ALL", _oldLcAll);
        Environment.SetEnvironmentVariable("LANG", _oldLang);
    }

    [Fact]
    public void Embedded_library_resolves_all_languages()
    {
        var eng = new LanguageProvider(LanguageType.eng);
        var fre = new LanguageProvider(LanguageType.fre);
        var chn = new LanguageProvider(LanguageType.chn_s);

        Assert.Equal("success", eng.GetLanguage("Cli.Success"));
        Assert.Equal("succes", fre.GetLanguage("Cli.Success"));
        Assert.Equal("成功", chn.GetLanguage("Cli.Success"));
    }

    [Fact]
    public void Missing_column_falls_back_to_english()
    {
        // CHN_T n'a pas de colonne dans la bibliothèque → repli ENG.
        var cht = new LanguageProvider(LanguageType.chn_t);
        Assert.Equal("success", cht.GetLanguage("Cli.Success"));
    }

    [Fact]
    public void Unknown_key_returns_the_key()
    {
        var provider = new LanguageProvider(LanguageType.eng);
        Assert.Equal("Cli.Does.Not.Exist", provider.GetLanguage("Cli.Does.Not.Exist"));
        Assert.Equal(string.Empty, provider.GetLanguage(""));
    }

    [Fact]
    public void Detect_follows_locale_environment()
    {
        Environment.SetEnvironmentVariable("LC_ALL", "fr_FR.UTF-8");
        Environment.SetEnvironmentVariable("LANG", null);
        Assert.Equal(LanguageType.fre, LanguageProvider.Detect());

        Environment.SetEnvironmentVariable("LC_ALL", "zh_CN.UTF-8");
        Assert.Equal(LanguageType.chn_s, LanguageProvider.Detect());

        Environment.SetEnvironmentVariable("LC_ALL", null);
        Environment.SetEnvironmentVariable("LANG", "en_US.UTF-8");
        Assert.Equal(LanguageType.eng, LanguageProvider.Detect());

        Environment.SetEnvironmentVariable("LANG", null);
        Assert.Equal(LanguageType.eng, LanguageProvider.Detect());
    }

    [Fact]
    public void User_library_in_xdg_config_overrides_embedded()
    {
        string configHome = Path.Combine(Path.GetTempPath(), "miflash-i18n-" + Path.GetRandomFileName());
        string miflashDir = Path.Combine(configHome, "miflash");
        Directory.CreateDirectory(miflashDir);
        File.WriteAllText(Path.Combine(miflashDir, "LanguageLibrary.xml"),
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<LanguageLibrary>\n" +
            "  <lan CTRLID=\"Cli.Success\" ENG=\"custom-ok\"></lan>\n</LanguageLibrary>\n");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);
        try
        {
            var provider = new LanguageProvider(LanguageType.eng);
            Assert.Equal("custom-ok", provider.GetLanguage("Cli.Success"));
            // Clé absente de la surcharge mais absente aussi → clé renvoyée.
            Assert.Equal("Cli.EducationalWarning", provider.GetLanguage("Cli.EducationalWarning"));
        }
        finally
        {
            Directory.Delete(configHome, recursive: true);
        }
    }
}
