using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

public class SaveCompactionTests
{
    private static SnowTag Tag(int counter, byte source = Snowport.HostSource) =>
        new(source, counter);

    private static Ref<T> Tag<T>(int counter, byte source = Snowport.HostSource)
        where T : Atom => new(Tag(counter, source));

    private static SnowportId Id(long clock, byte source = Snowport.HostSource) =>
        new(clock, source);

    private static ComponentPosition Position(int id) => new() { Id = Tag<Component>(id) };

    private static Component Component(int id) => new() { Id = Tag<Component>(id) };

    private static Prototype DeletedPrototype(int id) =>
        new() { Id = Tag<Prototype>(id), Deleted = true };

    private static TimelineEvent Save(params Atom[] atoms) =>
        new() { Id = Id(1_000_000, Snowport.AdminSource), Writes = atoms };

    private static TimelineEvent Load(string json) =>
        JsonSerializer.Deserialize<TimelineEvent>(json, LizzieJson.EventOptions);

    private static SnowTag IdOf(Atom atom) =>
        atom switch
        {
            ComponentPosition s => s.Id.Tag,
            Component c => c.Id.Tag,
            Stage s => s.Id.Tag,
            Prototype p => p.Id.Tag,
            Dataset d => d.Id.Tag,
            DataRow r => r.Id.Tag,
            DataColumn c => c.Id.Tag,
            _ => throw new ArgumentException($"No id for a {atom.GetType().Name}."),
        };

    private static DataCell Cell(int row, int column) =>
        new() { Id = new CellId(Tag<DataRow>(row), Tag<DataColumn>(column)), Value = "x" };

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
    public void ListedAtomsAreKept() => AssertKept([Position(1), Position(2)], 1, 2);

    [Fact]
    public void AnUnreferencedDeletedPrototypeIsDropped() =>
        AssertKept([Position(1), DeletedPrototype(2)], 1);

    [Fact]
    public void APositionKeepsItsComponent() => AssertKept([Position(1), Component(1)], 1, 1);

    [Fact]
    public void AComponentNothingPlacesIsDropped() => AssertKept([Position(1), Component(2)], 1);

    [Fact]
    public void ADeckKeepsTheCardsItIssued() =>
        AssertKept([Position(1), Component(2) with { DeckRef = Tag<Component>(1) }], 1, 2);

    [Fact]
    public void AnUnreferencedRetiredCardIsDropped() =>
        AssertKept(
            [Position(1), Component(2) with { DeckRef = Tag<Component>(1), Retired = true }],
            1
        );

    [Fact]
    public void ADeletedPrototypeAComponentUsesIsKept() =>
        AssertKept(
            [
                Position(1),
                Component(1) with
                {
                    PrototypeRef = Tag<Prototype>(2),
                },
                DeletedPrototype(2),
            ],
            1,
            1,
            2
        );

    [Fact]
    public void ABlueprintKeepsItsComponents() =>
        AssertKept(
            [
                new Stage { Id = Tag<Stage>(1) },
                Position(2) with
                {
                    StageRef = Tag<Stage>(1),
                },
                Position(3) with
                {
                    StageRef = Tag<Stage>(1),
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
                new Stage { Id = Tag<Stage>(1) },
                new Stage
                {
                    Id = Tag<Stage>(2),
                    ParentRef = Tag<Stage>(1),
                    Deleted = true,
                },
                Position(3) with
                {
                    StageRef = Tag<Stage>(2),
                },
            ],
            1
        );

    [Fact]
    public void ABlueprintUnderADeletedOneIsDroppedWithItsComponents() =>
        AssertKept(
            [
                new Stage { Id = Tag<Stage>(1) },
                new Stage
                {
                    Id = Tag<Stage>(2),
                    ParentRef = Tag<Stage>(1),
                    Deleted = true,
                },
                new Stage { Id = Tag<Stage>(3), ParentRef = Tag<Stage>(2) },
                Position(4) with
                {
                    StageRef = Tag<Stage>(3),
                },
            ],
            1
        );

    [Fact]
    public void ALoopOfBlueprintsIsDroppedWithItsComponents() =>
        AssertKept([
            new Stage { Id = Tag<Stage>(1), ParentRef = Tag<Stage>(2) },
            new Stage { Id = Tag<Stage>(2), ParentRef = Tag<Stage>(1) },
            Position(3) with
            {
                StageRef = Tag<Stage>(1),
            },
        ]);

    [Fact]
    public void ATableIsKeptWithItsComponents() =>
        AssertKept(
            [
                new Stage { Id = Tag<Stage>(1) },
                new Stage
                {
                    Id = Tag<Stage>(2),
                    ParentRef = Tag<Stage>(1),
                    IsTable = true,
                },
                Position(3) with
                {
                    StageRef = Tag<Stage>(1),
                },
                Position(3) with
                {
                    StageRef = Tag<Stage>(2),
                    X = 5,
                },
                Position(4) with
                {
                    StageRef = Tag<Stage>(2),
                },
            ],
            1,
            2,
            3,
            3,
            4
        );

    [Fact]
    public void ADatasetKeepsItsRows() =>
        AssertKept(
            [
                new Dataset { Id = Tag<Dataset>(1) },
                new DataRow { Id = Tag<DataRow>(2), DatasetRef = Tag<Dataset>(1) },
            ],
            1,
            2
        );

    [Fact]
    public void RowsOfAMissingDatasetAreDropped() =>
        AssertKept([new DataRow { Id = Tag<DataRow>(2), DatasetRef = Tag<Dataset>(1) }]);

    [Fact]
    public void ADeletedRowIsDropped() =>
        AssertKept(
            [
                new Dataset { Id = Tag<Dataset>(1) },
                new DataRow
                {
                    Id = Tag<DataRow>(2),
                    DatasetRef = Tag<Dataset>(1),
                    Deleted = true,
                },
            ],
            1
        );

    [Fact]
    public void ADeletedRowACardShowsIsKept() =>
        AssertKept(
            [
                new Dataset { Id = Tag<Dataset>(1) },
                new DataRow
                {
                    Id = Tag<DataRow>(2),
                    DatasetRef = Tag<Dataset>(1),
                    Deleted = true,
                },
                Position(3),
                Component(3) with
                {
                    Item = new PrintedItemRef(new RowFace(Tag<DataRow>(2)), null),
                },
            ],
            1,
            2,
            3,
            3
        );

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void ACellIsKeptOnlyWithItsRowAndColumn(bool row, bool column, bool kept)
    {
        Atom[] atoms =
        [
            new Dataset { Id = Tag<Dataset>(1) },
            .. row
                ? [new DataRow { Id = Tag<DataRow>(2), DatasetRef = Tag<Dataset>(1) }]
                : Array.Empty<Atom>(),
            .. column
                ? [new DataColumn { Id = Tag<DataColumn>(3), DatasetRef = Tag<Dataset>(1) }]
                : Array.Empty<Atom>(),
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
                Position(9) with
                {
                    ContainerRef = Tag(5, source: 3),
                },
                Position(7) with
                {
                    ContainerRef = Tag(9),
                }
            )
        );

        var back = Load(json).Writes.Cast<ComponentPosition>().ToArray();

        // in order: 7 < 9 < (source 3, 5)
        Assert.Equal(new SnowTag(2), back[0].Id.Tag);
        Assert.Equal(new SnowTag(3), back[0].ContainerRef);
        Assert.Equal(new SnowTag(1), back[1].Id.Tag);
        Assert.Equal(new SnowTag(2), back[1].ContainerRef);
        Assert.All(back, c => Assert.Equal(Snowport.AdminSource, c.Id.Tag.source));
    }

    [Fact]
    public void CellIdsAreRenumbered()
    {
        var json = SaveCompaction.Serialize(
            Save(
                new DataRow { Id = Tag<DataRow>(41), DatasetRef = Tag<Dataset>(40) },
                new DataColumn { Id = Tag<DataColumn>(50), DatasetRef = Tag<Dataset>(40) },
                Cell(41, 50)
            )
        );

        var back = Load(json).Writes;
        var row = Assert.IsType<DataRow>(back[0]);
        var column = Assert.IsType<DataColumn>(back[1]);
        var cell = Assert.IsType<DataCell>(back[2]);
        Assert.Equal(new CellId(row.Id, column.Id), cell.Id);
        Assert.Equal(new SnowTag(3), column.Id.Tag);
    }

    [Fact]
    public void SnowportIdsBecomeAdminIdsFromOneAndKeepStackingOrder()
    {
        var below = new ZOrder(ZTarget.Top, 0, Id(500, source: 2));
        var above = new ZOrder(ZTarget.Top, 0, Id(900, source: 1));
        var json = SaveCompaction.Serialize(
            Save(Position(1) with { ZOrder = above }, Position(2) with { ZOrder = below })
        );

        var back = Load(json);
        var components = back.Writes.Cast<ComponentPosition>().ToArray();

        Assert.True(back.IsAdmin, "The save event should stay an admin event.");
        Assert.Equal(Id(1, Snowport.AdminSource), components[1].ZOrder.Stamp);
        Assert.Equal(Id(2, Snowport.AdminSource), components[0].ZOrder.Stamp);
        Assert.Equal(Id(3, Snowport.AdminSource), back.Id);
        Assert.True(components[0].ZOrder > components[1].ZOrder, "Stacking order changed.");
    }

    [Fact]
    public void UnsetIdsStayUnset()
    {
        var json = SaveCompaction.Serialize(Save(Position(1)));
        var back = (ComponentPosition)Load(json).Writes[0];

        Assert.Equal(SnowTag.Empty, back.ContainerRef);
        Assert.Equal(SnowportId.Empty, back.ZOrder.Stamp);
    }

    [Fact]
    public void SavingALoadedSaveChangesNothing()
    {
        var first = SaveCompaction.Serialize(
            Save(
                Position(8) with
                {
                    ZOrder = new ZOrder(ZTarget.Bottom, 1, Id(77)),
                },
                Component(8) with
                {
                    PrototypeRef = Tag<Prototype>(3, source: 2),
                },
                new Prototype { Id = Tag<Prototype>(3, source: 2), Name = "Card" }
            )
        );

        var second = SaveCompaction.Serialize(Load(first));

        Assert.Equal(first, second);
    }
}
