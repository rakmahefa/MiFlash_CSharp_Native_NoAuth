namespace MiFlash.Cli.i18n;

/// <summary>
/// Façade statique de localisation (M4.6) : <c>L._("Cli.Warning")</c>.
/// Le fournisseur par défaut suit la locale ; <see cref="Provider"/> est
/// remplaçable (option globale <c>--lang</c>).
/// </summary>
public static class L
{
    private static LanguageProvider _provider = new(LanguageProvider.Detect());

    public static LanguageProvider Provider
    {
        get => _provider;
        set => _provider = value ?? new LanguageProvider(LanguageType.eng);
    }

    public static string _(string ctrlID)
    {
        return _provider.GetLanguage(ctrlID);
    }
}
