namespace MiFlash.Core.Io;

/// <summary>
/// Résolution sécurisée de chemins issus de fichiers XML externes
/// (rawprogram/patch, restore, FFU) — durcissement M6.5.
/// Un attribut « filename » malveillant ne doit pas permettre d'échapper au
/// répertoire d'images : traversée de chemin (« ../.. ») ou chemin
/// absolu/UNC ne sont pas acceptés. L'outil Windows d'origine combinait
/// directement <c>Path.Combine</c> sans contrôle.
/// </summary>
public static class SecurePath
{
    /// <summary>
    /// Combine <paramref name="basePath"/> et <paramref name="name"/> en
    /// garantissant que le résultat reste sous <paramref name="basePath"/>
    /// (après résolution des segments « . »/« .. »). Les sous-répertoires
    /// relatifs légitimes (« Images/system.img ») restent autorisés.
    /// </summary>
    /// <param name="basePath">Répertoire racine autorisé (répertoire d'images).</param>
    /// <param name="name">Nom de fichier lu dans le XML.</param>
    /// <returns>Chemin complet garanti sous <paramref name="basePath"/>.</returns>
    /// <exception cref="IOException">Nom vide, chemin absolu/UNC, ou traversée
    /// hors de <paramref name="basePath"/>.</exception>
    public static string Combine(string basePath, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new IOException("empty file name in XML");
        }

        if (Path.IsPathRooted(name))
        {
            throw new IOException($"refusing absolute path from XML: {name}");
        }

        basePath = Path.GetFullPath(basePath);
        string full = Path.GetFullPath(Path.Combine(basePath, name));

        string basePrefix = basePath.EndsWith(Path.DirectorySeparatorChar)
            ? basePath
            : basePath + Path.DirectorySeparatorChar;

        if (!full.StartsWith(basePrefix, StringComparison.Ordinal))
        {
            throw new IOException($"path escapes image directory ({basePath}): {name}");
        }

        return full;
    }
}
