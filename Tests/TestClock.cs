using System.Runtime.CompilerServices;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Gives the stamps a time that doesn't need the engine, before any test runs.
/// It stands still, so the stamps made just count up.
/// </summary>
internal static class TestClock
{
    [ModuleInitializer]
    internal static void Use() => SnowStamp.TicksMsec = () => 0;
}

/// <summary>
/// The tests that set <see cref="SnowStamp.TicksMsec"/> or <see cref="Source.Me"/>, which run alone, since every other test mints with them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ClockSwaps
{
    public const string Name = "Clock swaps";
}
