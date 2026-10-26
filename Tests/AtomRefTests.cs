using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Lizzie.Tests;

public class AtomRefTests
{
    [StackTraceHidden]
    private static void AssertRoundTrips(IAtomRef atom, string expected)
    {
        var json = JsonSerializer.Serialize(atom, LizzieJson.EventOptions);
        var back = JsonSerializer.Deserialize<IAtomRef>(json, LizzieJson.EventOptions);
        Assert.True(
            json == expected && atom.Equals(back),
            $"A ref to a {atom.AtomType.Name} {atom} was sent as {json} and read back as a {back?.AtomType.Name} {back}.\n"
                + $"Expected it sent as {expected}."
        );
    }

    [Fact]
    public void ARefIsSentAsItsTypeAndId() =>
        AssertRoundTrips(new Ref<DataRow>(5), """{"type":"DataRow","id":5}""");

    [Fact]
    public void ARefKeepsAStructId() =>
        AssertRoundTrips(
            new CellId(new(5), new(6)),
            """{"type":"DataCell","id":{"rowRef":5,"columnRef":6}}"""
        );

    [Fact]
    public void ARefToAStagedAtomKeepsItsStage() =>
        AssertRoundTrips(
            new StageKey<ComponentPosition>(new(5), new(6)),
            """{"type":"ComponentPosition","id":{"stageRef":5,"componentRef":6}}"""
        );

    [Fact]
    public void ARefCantPointAtAStagedAtom()
    {
        var error = Record.Exception(() => new Ref<ComponentPosition>(1));
        Assert.True(
            error is TypeInitializationException { InnerException: InvalidOperationException },
            $"A Ref<ComponentPosition> was made, but a tag alone doesn't find a staged atom. It threw {error?.GetType().Name ?? "nothing"}."
        );
    }

    [Fact]
    public void ASelectionKeepsItsRefs()
    {
        var targets = ImmutableHashSet.Create<IAtomRef>(
            new StageKey<ComponentPosition>(new(2), new(1)),
            new Ref<DataColumn>(1)
        );
        var json = JsonSerializer.Serialize<Atom>(
            new Selection { Id = new(9), Targets = targets },
            LizzieJson.EventOptions
        );
        var back = (Selection)JsonSerializer.Deserialize<Atom>(json, LizzieJson.EventOptions);
        Assert.True(
            back.Targets.SetEquals(targets),
            $"A selection of a component and a column with the same tag came back as {string.Join(", ", back.Targets.Select(t => $"{t.AtomType.Name} {t}"))}."
        );
    }
}
