using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>
/// Collection sérialisée pour les classes de tests qui mutent l'environnement
/// du process (XDG_*, LC_ALL/LANG, PATH). xUnit parallélise les collections
/// par défaut : sans cela, <c>AppPathsTests</c> et <c>LanguageProviderTests</c>
/// se marchent dessus sur XDG_CONFIG_HOME (race observée en CI sur bee89b4,
/// run 42972193581 — 134/135 verts localement mais aléatoires en runner).
/// </summary>
[CollectionDefinition(Name = "env-serial", DisableParallelization = true)]
public class EnvSerialCollection
{
}
