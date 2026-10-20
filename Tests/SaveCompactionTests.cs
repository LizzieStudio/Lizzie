using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Lizzie.Replication.Machinery;
using Xunit;

public class SaveCompactionTests
{
    private static SnowTag Tag(int counter, byte source = Snowport.HostSource) =>
        new(source, counter);

    private static SnowportId Id(long clock, byte source = Snowport.HostSource) =>
        new(clock, source);

    private static ComponentState Component(int id) => new() { Id = Tag(id) };

    private static Prototype DeletedPrototype(int id) => new() { Id = Tag(id), Deleted = true };

    private static TableEvent Save(params Atom[] atoms) =>
        new() { Id = Id(1_000_000, Snowport.AdminSource), Writes = atoms };

    private static TableEvent Load(string json) =>
        JsonSerializer.Deserialize<TableEvent>(json, LizzieJson.EventOptions);

    private static SnowTag IdOf(Atom atom) =>
        atom switch
        {
            ComponentState s => s.Id,
            Stage s => s.Id,
            Prototype p => p.Id,
            DataSet d => d.Id,
            DataRow r => r.Id,
            DataColumn c => c.Id,
            _ => throw new ArgumentException($"No id for a {atom.GetType().Name}."),
        };

    private static DataCell Cell(int row, int column) =>
        new() { Id = new CellId(Tag(row), Tag(column)), Value = "x" };

    [StackTraceHidden]
    private static void AssertKept(IEnumerable<Atom> atoms, params int[] expected)
    {
        var kept = SaveCompaction.Collect(atoms).Select(IdOf).OrderBy(t => t).ToArray();
        var want = expected.Select(n => Tag(n)).OrderBy(t => t).ToArray();
        Assert.True(
            kept.SequenceEqual(want),
            $"Kept the wrong atoms.\n\nExpected: {string.Join(", ", want)}\nKept:     {string.Join(", ", kept)}"
        );
    }

    [Fact]
    public void ListedAtomsAreKept() => AssertKept([Component(1), Component(2)], 1, 2);

    [Fact]
    public void AnUnreferencedDeletedPrototypeIsDropped() =>
        AssertKept([Component(1), DeletedPrototype(2)], 1);

    [Fact]
    public void ADeletedPrototypeAComponentUsesIsKept() =>
        AssertKept([Component(1) with { PrototypeRef = Tag(2) }, DeletedPrototype(2)], 1, 2);

    [Fact]
    public void ABlueprintKeepsItsComponents() =>
        AssertKept(
            [
                new Stage { Id = Tag(1) },
                Component(2) with
                {
                    Stage = Tag(1),
                },
                Component(3) with
                {
                    Stage = Tag(1),
                    Removed = true,
                },
            ],
            1,
            2,
            3
        );

    [Fact]
    public void ADeletedBlueprintIsDroppedWithItsComponents() =>
        AssertKept(
            [
                new Stage { Id = Tag(1) },
                new Stage
                {
                    Id = Tag(2),
                    ParentRef = Tag(1),
                    Deleted = true,
                },
                Component(3) with
                {
                    Stage = Tag(2),
                },
            ],
            1
        );

    [Fact]
    public void ABlueprintUnderADeletedOneIsDroppedWithItsComponents() =>
        AssertKept(
            [
                new Stage { Id = Tag(1) },
                new Stage
                {
                    Id = Tag(2),
                    ParentRef = Tag(1),
                    Deleted = true,
                },
                new Stage { Id = Tag(3), ParentRef = Tag(2) },
                Component(4) with
                {
                    Stage = Tag(3),
                },
            ],
            1
        );

    [Fact]
    public void ALoopOfBlueprintsIsDroppedWithItsComponents() =>
        AssertKept([
            new Stage { Id = Tag(1), ParentRef = Tag(2) },
            new Stage { Id = Tag(2), ParentRef = Tag(1) },
            Component(3) with
            {
                Stage = Tag(1),
            },
        ]);

    [Fact]
    public void AGameIsDroppedWithItsComponents() =>
        AssertKept(
            [
                new Stage { Id = Tag(1) },
                new Stage
                {
                    Id = Tag(2),
                    ParentRef = Tag(1),
                    IsGame = true,
                },
                Component(3) with
                {
                    Stage = Tag(1),
                },
                Component(3) with
                {
                    Stage = Tag(2),
                    X = 5,
                },
                Component(4) with
                {
                    Stage = Tag(2),
                },
            ],
            1,
            3
        );

    [Fact]
    public void ADataSetKeepsItsRows() =>
        AssertKept(
            [new DataSet { Id = Tag(1) }, new DataRow { Id = Tag(2), DataSetId = Tag(1) }],
            1,
            2
        );

    [Fact]
    public void RowsOfAMissingDataSetAreDropped() =>
        AssertKept([new DataRow { Id = Tag(2), DataSetId = Tag(1) }]);

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void ACellIsKeptOnlyWithItsRowAndColumn(bool row, bool column, bool kept)
    {
        Atom[] atoms =
        [
            new DataSet { Id = Tag(1) },
            .. row ? [new DataRow { Id = Tag(2), DataSetId = Tag(1) }] : Array.Empty<Atom>(),
            .. column ? [new DataColumn { Id = Tag(3), DataSetId = Tag(1) }] : Array.Empty<Atom>(),
            Cell(2, 3),
        ];
        var cells = SaveCompaction.Collect(atoms).OfType<DataCell>().Count();
        Assert.True(
            cells == (kept ? 1 : 0),
            $"With its row {(row ? "kept" : "gone")} and its column {(column ? "kept" : "gone")}, "
                + $"the save kept {cells} cells, expected {(kept ? 1 : 0)}."
        );
    }

    [Fact]
    public void TagsBecomeAdminTagsFromOneInOrder()
    {
        var json = SaveCompaction.Serialize(
            Save(
                Component(9) with
                {
                    ContainerRef = Tag(5, source: 3),
                },
                Component(7) with
                {
                    PrototypeRef = Tag(9),
                }
            )
        );

        var back = Load(json).Writes.Cast<ComponentState>().ToArray();

        // in order: 7 < 9 < (source 3, 5)
        Assert.Equal(new SnowTag(2), back[0].Id);
        Assert.Equal(new SnowTag(3), back[0].ContainerRef);
        Assert.Equal(new SnowTag(1), back[1].Id);
        Assert.Equal(new SnowTag(2), back[1].PrototypeRef);
        Assert.All(back, c => Assert.Equal(Snowport.AdminSource, c.Id.source));
        Assert.Equal(SnowTag.Empty, back[1].ContainerRef);
    }

    [Fact]
    public void CellIdsAreRenumbered()
    {
        var json = SaveCompaction.Serialize(
            Save(
                new DataRow { Id = Tag(41), DataSetId = Tag(40) },
                new DataColumn { Id = Tag(50), DataSetId = Tag(40) },
                Cell(41, 50)
            )
        );

        var back = Load(json).Writes;
        var row = Assert.IsType<DataRow>(back[0]);
        var column = Assert.IsType<DataColumn>(back[1]);
        var cell = Assert.IsType<DataCell>(back[2]);
        Assert.Equal(new CellId(row.Id, column.Id), cell.Id);
        Assert.Equal(new SnowTag(3), column.Id);
    }

    [Fact]
    public void SnowportIdsBecomeAdminIdsFromOneAndKeepStackingOrder()
    {
        var below = new ZOrder(ZTarget.Top, 0, Id(500, source: 2));
        var above = new ZOrder(ZTarget.Top, 0, Id(900, source: 1));
        var json = SaveCompaction.Serialize(
            Save(Component(1) with { ZOrder = above }, Component(2) with { ZOrder = below })
        );

        var back = Load(json);
        var components = back.Writes.Cast<ComponentState>().ToArray();

        Assert.True(back.IsAdmin, "The save event should stay an admin event.");
        Assert.Equal(Id(1, Snowport.AdminSource), components[1].ZOrder.Stamp);
        Assert.Equal(Id(2, Snowport.AdminSource), components[0].ZOrder.Stamp);
        Assert.Equal(Id(3, Snowport.AdminSource), back.Id);
        Assert.True(components[0].ZOrder > components[1].ZOrder, "Stacking order changed.");
    }

    [Fact]
    public void UnsetIdsStayUnset()
    {
        var json = SaveCompaction.Serialize(Save(Component(1)));
        var back = (ComponentState)Load(json).Writes[0];

        Assert.Equal(SnowTag.Empty, back.PrototypeRef);
        Assert.Equal(SnowportId.Empty, back.ZOrder.Stamp);
    }

    [Fact]
    public void SavingALoadedSaveChangesNothing()
    {
        var first = SaveCompaction.Serialize(
            Save(
                Component(8) with
                {
                    PrototypeRef = Tag(3, source: 2),
                    ZOrder = new ZOrder(ZTarget.Bottom, 1, Id(77)),
                },
                new Prototype { Id = Tag(3, source: 2), Name = "Card" }
            )
        );

        var second = SaveCompaction.Serialize(Load(first));

        Assert.Equal(first, second);
    }
}
