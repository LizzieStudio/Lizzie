using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Lizzie.Tests;

public class AtomRefTests
{
    [StackTraceHidden]
    private static void AssertRoundTrips(AtomRef atom, string expected)
    {
        var json = JsonSerializer.Serialize(atom, LizzieJson.EventOptions);
        var back = JsonSerializer.Deserialize<AtomRef>(json, LizzieJson.EventOptions);
        Assert.True(
            json == expected && back == atom,
            $"A ref to a {atom.Type.Name} {atom.Id} was sent as {json} and read back as a {back.Type?.Name} {back.Id}.\n"
                + $"Expected it sent as {expected}."
        );
    }

    [Fact]
    public void ARefIsSentAsItsTypeAndId() =>
        AssertRoundTrips(DataRow.ById.Ref(new SnowTag(5)), """{"type":"DataRow","id":5}""");

    [Fact]
    public void ARefKeepsAStructId() =>
        AssertRoundTrips(
            DataCell.ById.Ref(new CellId(new SnowTag(5), new SnowTag(6))),
            """{"type":"DataCell","id":{"row":5,"column":6}}"""
        );

    [Fact]
    public void ARefToAStagedAtomKeepsItsStage() =>
        AssertRoundTrips(
            ComponentPosition.ById.Ref(new StageKey(new SnowTag(5), new SnowTag(6))),
            """{"type":"ComponentPosition","id":{"stage":5,"component":6}}"""
        );

    [Fact]
    public void ASelectionKeepsItsRefs()
    {
        var targets = ImmutableHashSet.Create(
            ComponentPosition.ById.Ref(new StageKey(new SnowTag(2), new SnowTag(1))),
            DataColumn.ById.Ref(new SnowTag(1))
        );
        var json = JsonSerializer.Serialize<Atom>(
            new Selection { Id = new SnowTag(9), Targets = targets },
            LizzieJson.EventOptions
        );
        var back = (Selection)JsonSerializer.Deserialize<Atom>(json, LizzieJson.EventOptions);
        Assert.True(
            back.Targets.SetEquals(targets),
            $"A selection of a component and a column with the same tag came back as {string.Join(", ", back.Targets.Select(t => $"{t.Type.Name} {t.Id}"))}."
        );
    }
}
