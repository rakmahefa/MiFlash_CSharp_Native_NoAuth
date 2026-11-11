using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace MiFlash.Cli.i18n;

/// <summary>
/// Résolution des libellés localisés (M4.6) — port de
/// <c>XiaoMiFlash.code.lan.LanguageProvider</c>. Écarts documentés :
/// <list type="bullet">
/// <item>le XML n'est plus rechargé à chaque appel : le document est mis en
/// cache (l'original relisait <c>LanguageLibrary.xml</c> par <c>GetLanguage</c>) ;</item>
/// <item>le chemin <c>AppDomain…ApplicationBase + "Source\LanguageLibrary.xml"</c>
/// devient une ressource embarquée, surchargeable par
/// <c>$XDG_CONFIG_HOME/miflash/LanguageLibrary.xml</c> ;</item>
/// <item>chaîne de repli : langue demandée → colonne ENG → la clé elle-même
/// (l'original renvoyait une chaîne vide).</item>
/// </list>
/// </summary>
public sealed class LanguageProvider
{
    private readonly string _languageType;
    private readonly string _libraryPath;
    private XmlDocument _cache;

    public LanguageProvider(string lanType)
    {
        _languageType = lanType ?? throw new ArgumentNullException(nameof(lanType));
        _libraryPath = ResolveUserLibrary();
    }

    /// <summary>Nom de colonne actif (ENG/FRE/CHN_S/CHN_T).</summary>
    public string ActiveLanguage => _languageType;

    public string GetLanguage(string ctrlID)
    {
        if (string.IsNullOrEmpty(ctrlID))
        {
            return string.Empty;
        }
        XmlDocument doc = LoadLibrary();
        XmlNodeList childNodes = doc.SelectSingleNode("LanguageLibrary")?.ChildNodes;
        if (childNodes != null)
        {
            foreach (XmlNode item in childNodes)
            {
                if (item.Name.ToLowerInvariant() != "lan")
                {
                    continue;
                }
                if (item.Attributes?["CTRLID"]?.Value == ctrlID)
                {
                    string value = item.Attributes[_languageType]?.Value;
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                    // Repli : colonne anglaise, puis la clé (l'original : vide).
                    return item.Attributes[LanguageType.eng]?.Value is string eng && eng.Length > 0 ? eng : ctrlID;
                }
            }
        }
        return ctrlID;
    }

    private XmlDocument LoadLibrary()
    {
        if (_cache != null)
        {
            return _cache;
        }
        var doc = new XmlDocument();
        if (_libraryPath != null && File.Exists(_libraryPath))
        {
            using var reader = XmlReader.Create(_libraryPath, new XmlReaderSettings { IgnoreComments = true });
            doc.Load(reader);
        }
        else
        {
            Assembly assembly = typeof(LanguageProvider).Assembly;
            using Stream stream = assembly.GetManifestResourceStream("MiFlash.Cli.i18n.LanguageLibrary.xml")
                ?? throw new InvalidOperationException("Ressource embarquee LanguageLibrary.xml introuvable");
            doc.Load(stream);
        }
        _cache = doc;
        return doc;
    }

    /// <summary>Bibliothèque utilisateur : <c>$XDG_CONFIG_HOME/miflash/LanguageLibrary.xml</c>.</summary>
    private static string ResolveUserLibrary()
    {
        string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome) || !Path.IsPathRooted(configHome))
        {
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(configHome, "miflash", "LanguageLibrary.xml");
    }

    /// <summary>
    /// Langue par défaut depuis l'environnement (<c>LC_ALL</c> puis
    /// <c>LANG</c>) : <c>fr*</c> → FRE, <c>zh*</c> → CHN_S, sinon ENG.
    /// </summary>
    public static string Detect()
    {
        string locale = Environment.GetEnvironmentVariable("LC_ALL")
                        ?? Environment.GetEnvironmentVariable("LANG")
                        ?? string.Empty;
        if (locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
        {
            return LanguageType.fre;
        }
        if (locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return LanguageType.chn_s;
        }
        return LanguageType.eng;
    }
}
