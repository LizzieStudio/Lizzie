using System.Runtime.CompilerServices;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Gives the tests a clock that doesn't need the engine, before any test runs.
/// Its time stands still, so the ids it makes just count up.
/// </summary>
internal static class TestClock
{
    [ModuleInitializer]
    internal static void Use() => Snowport.Clock = new Snowport(Snowport.HostSource, () => 0);
}

/// <summary>
/// The tests that set <see cref="Snowport.Clock"/>, which run alone, since every other test mints from it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ClockSwaps
{
    public const string Name = "Clock swaps";
}
