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
    private static SnowTag Tag(int counter, Source? source = null) =>
        new(source ?? Source.Host, counter);

    private static Ref<T> Tag<T>(int counter, Source? source = null)
        where T : Atom => new(Tag(counter, source));

    private static SnowStamp Id(long clock, Source? source = null) =>
        new(clock, source ?? Source.Host);

    private static ComponentPosition Position(int id) => new() { Id = Tag<Component>(id) };

    private static Component Component(int id) => new() { Id = Tag<Component>(id) };

    private static Prototype DeletedPrototype(int id) =>
        new() { Id = Tag<Prototype>(id), Deleted = true };

    private static TimelineEvent Save(params Atom[] atoms) =>
        new() { Id = Id(1_000_000, Source.Admin), Writes = atoms };

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
            Seat s => s.Id.Tag,
            DataRow r => r.Id.Tag,
            DataColumn c => c.Id.Tag,
            Notes n => n.Id.Tag,
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
    public void ADeletedSeatAHandIsInIsKept() =>
        AssertKept(
            [
                Position(1) with
                {
                    Location = new InHand(Tag<Seat>(2)),
                },
                new Seat { Id = Tag<Seat>(2), Deleted = true },
            ],
            1,
            2
        );

    [Fact]
    public void AnUnreferencedDeletedSeatIsDropped() =>
        AssertKept([Position(1), new Seat { Id = Tag<Seat>(2), Deleted = true }], 1);

    [Fact]
    public void AZoneRuleKeepsNoDeletedSeat() =>
        AssertKept(
            [
                new Prototype
                {
                    Id = Tag<Prototype>(1),
                    Parameters = new ZoneParameters
                    {
                        Seats = WeakMap<Ref<Seat>, ZoneAccess>.Empty.With(
                            Tag<Seat>(2),
                            ZoneAccess.Blocked
                        ),
                    },
                },
                new Seat { Id = Tag<Seat>(2), Deleted = true },
            ],
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
            [new Stage { Id = Tag<Stage>(1) }, Position(2) with { StageRef = Tag<Stage>(1) }],
            1,
            2
        );

    [Fact]
    public void WhatsInAKeptComponentIsKept() =>
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
                    Location = new InComponent(Tag<Component>(2)),
                },
            ],
            1,
            2,
            3
        );

    [Fact]
    public void WhatsInADroppedComponentIsDroppedThoughItRefersToIt() =>
        AssertKept(
            [
                new Stage { Id = Tag<Stage>(1) },
                Position(2) with
                {
                    StageRef = Tag<Stage>(1),
                    Removed = true,
                },
                Component(2),
                Position(3) with
                {
                    StageRef = Tag<Stage>(1),
                    Location = new InComponent(Tag<Component>(2)),
                },
                Component(3) with
                {
                    DeckRef = Tag<Component>(2),
                },
            ],
            1
        );

    [Fact]
    public void ARemovalOfAComponentPlacedAboveIsKept() =>
        AssertKept(
            [
                new Stage { Id = Tag<Stage>(1) },
                new Stage { Id = Tag<Stage>(2), ParentRef = Tag<Stage>(1) },
                Position(3) with
                {
                    StageRef = Tag<Stage>(1),
                },
                Removal(2, 3),
            ],
            1,
            2,
            3,
            3
        );

    [Fact]
    public void AWriteInADeletedBlueprintIsDroppedThoughItsComponentIsPlacedElsewhere() =>
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
                    StageRef = Tag<Stage>(1),
                },
                Position(3) with
                {
                    StageRef = Tag<Stage>(2),
                    X = 5,
                },
            ],
            1,
            3
        );

    [Fact]
    public void ARemovalOfAComponentPlacedNowhereIsDropped() =>
        AssertKept([new Stage { Id = Tag<Stage>(1) }, Removal(1, 3)], 1);

    [Fact]
    public void ARemovalOfACardItsDeckStillKeepsIsDropped() =>
        AssertKept(
            [
                new Stage { Id = Tag<Stage>(1) },
                Position(2) with
                {
                    StageRef = Tag<Stage>(1),
                },
                Component(3) with
                {
                    DeckRef = Tag<Component>(2),
                },
                Removal(1, 3),
            ],
            1,
            2,
            3
        );

    /// <summary>
    /// A table moved a blueprint's card into a deck it made, then deleted the deck.
    /// The card still hides the blueprint's copy, but nothing keeps the deck.
    /// </summary>
    [Fact]
    public void WhatsInADroppedComponentIsKeptAsARemovalWhenItHidesACopyAbove()
    {
        var hidden = Position(3) with
        {
            StageRef = Tag<Stage>(2),
            Location = new InComponent(Tag<Component>(4)),
            X = 7,
        };
        var kept = SaveCompaction.Collect([
            new Stage { Id = Tag<Stage>(1) },
            new Stage { Id = Tag<Stage>(2), ParentRef = Tag<Stage>(1) },
            Position(3) with
            {
                StageRef = Tag<Stage>(1),
            },
            hidden,
            Removal(2, 4),
            Component(4),
            Position(5) with
            {
                StageRef = Tag<Stage>(2),
                Location = new InComponent(Tag<Component>(4)),
            },
        ]);

        Assert.Equal(
            new[]
            {
                Position(3) with
                {
                    StageRef = Tag<Stage>(1),
                },
                new ComponentPosition
                {
                    Id = Tag<Component>(3),
                    StageRef = Tag<Stage>(2),
                    Removed = true,
                },
            },
            kept.OfType<ComponentPosition>().OrderBy(p => p.StageRef.Tag)
        );
        Assert.Empty(kept.OfType<Component>());
    }

    private static ComponentPosition Removal(int stage, int id) =>
        (Position(id) with { StageRef = Tag<Stage>(stage) }).Removal();

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
                    Location = new InComponent(Tag<Component>(5, source: new Source(3))),
                },
                Position(7) with
                {
                    Location = new InComponent(Tag<Component>(9)),
                }
            )
        );

        var back = Load(json).Writes.Cast<ComponentPosition>().ToArray();

        // in order: 7 < 9 < (source 3, 5)
        Assert.Equal(new SnowTag(2), back[0].Id.Tag);
        Assert.Equal(new InComponent(new(new SnowTag(3))), back[0].Location);
        Assert.Equal(new SnowTag(1), back[1].Id.Tag);
        Assert.Equal(new InComponent(new(new SnowTag(2))), back[1].Location);
        Assert.All(back, c => Assert.Equal(Source.Admin, c.Id.Tag.Source));
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
    public void SnowStampsAreSavedAsTheyAre()
    {
        var rank = SnowRank.AfterAll(1)[0];
        var json = SaveCompaction.Serialize(Save(Position(1) with { Rank = rank }));

        var back = Load(json);

        Assert.True(back.IsAdmin, "The save event should stay an admin event.");
        Assert.Equal(Id(1_000_000, Source.Admin), back.Id);
        Assert.Equal(rank, ((ComponentPosition)back.Writes[0]).Rank);
    }

    [Fact]
    public void TheSaveTakesTheClockOfTheNewestWrite()
    {
        var save = SaveCompaction.Event([
            new AtomEntry(Position(1), Id(700, source: new Source(3))),
            new AtomEntry(Position(2), Id(900, source: new Source(2))),
            new AtomEntry(Position(3), Id(800)),
        ]);

        Assert.Equal(Id(900, Source.Admin), save.Id);
    }

    [Fact]
    public void UnsetIdsStayUnset()
    {
        var json = SaveCompaction.Serialize(Save(Position(1)));
        var back = (ComponentPosition)Load(json).Writes[0];

        Assert.Equal(Ref<Stage>.None, back.StageRef);
        Assert.Equal(default, back.Rank);
    }

    [Fact]
    public void AWeakMapKeyKeepsNothing()
    {
        var side = new QuickSide { Suits = WeakMap<SnowTag, QuickSuit>.Empty.With(Tag(2), new()) };
        AssertKept(
            [
                new Prototype
                {
                    Id = Tag<Prototype>(1),
                    Parameters = new PrintedParameters { Front = side },
                },
                DeletedPrototype(2),
            ],
            1
        );
    }

    /// <summary>A listed atom with notes about prototypes, each naming another prototype.</summary>
    public sealed record Notes : Atom
    {
        public static readonly Identity<Notes, Ref<Notes>> ById = new(n => n.Id);

        public static readonly All<Notes> All = new();

        public required Ref<Notes> Id { get; init; }

        public WeakMap<Ref<Prototype>, Ref<Prototype>> About { get; init; } =
            WeakMap<Ref<Prototype>, Ref<Prototype>>.Empty;
    }

    /// <summary>Notes, kept as atom 1, saying each prototype in a pair names the other.</summary>
    private static Notes NotesOn(params (int About, int Names)[] notes) =>
        new()
        {
            Id = Tag<Notes>(1),
            About = notes.Aggregate(
                WeakMap<Ref<Prototype>, Ref<Prototype>>.Empty,
                (map, n) => map.With(Tag<Prototype>(n.About), Tag<Prototype>(n.Names))
            ),
        };

    [Fact]
    public void AWeakValueKeepsWhatItNamesOnceItsKeyIsKept() =>
        AssertKept(
            [NotesOn((2, 3)), new Prototype { Id = Tag<Prototype>(2) }, DeletedPrototype(3)],
            1,
            2,
            3
        );

    [Fact]
    public void AWeakValueKeepsNothingWhileItsKeyIsDropped() =>
        AssertKept([NotesOn((2, 3)), DeletedPrototype(2), DeletedPrototype(3)], 1);

    /// <summary>
    /// The note about 2 keeps 3, which keeps the note about 3, which keeps 4.
    /// Nothing reaches 5, so its note keeps nothing.
    /// </summary>
    [Fact]
    public void AWeakValueCanKeepAnotherEntrysKey() =>
        AssertKept(
            [
                NotesOn((2, 3), (3, 4), (5, 6)),
                new Prototype { Id = Tag<Prototype>(2) },
                DeletedPrototype(3),
                DeletedPrototype(4),
                DeletedPrototype(5),
                DeletedPrototype(6),
            ],
            1,
            2,
            3,
            4
        );

    private static WeakMap<SnowTag, string> Weak(params int[] keys) =>
        keys.Aggregate(WeakMap<SnowTag, string>.Empty, (map, key) => map.With(Tag(key), $"{key}"));

    [Fact]
    public void ASaveWritesOnlyTheWeakEntriesWhoseKeysItKeeps()
    {
        var renumber = new Dictionary<SnowTag, SnowTag> { [Tag(2)] = new(Source.Admin, 1) };

        var json = JsonSerializer.Serialize(Weak(1, 2, 3), LizzieJson.SaveOptions(renumber));

        Assert.True(
            json == $"{{\"{new SnowTag(Source.Admin, 1)}\":\"2\"}}",
            $"Wrote the weak map as {json}, expected only the entry for the kept key, renumbered."
        );
    }

    [Fact]
    public void AMessageWritesEveryWeakEntry()
    {
        var json = JsonSerializer.Serialize(Weak(1, 2), LizzieJson.EventOptions);
        var back = JsonSerializer.Deserialize<WeakMap<SnowTag, string>>(
            json,
            LizzieJson.EventOptions
        );

        Assert.True(
            back.TryGetValue(Tag(1), out var one) && one == "1" && back[Tag(2)] == "2",
            $"Sent the weak map as {json}, expected both entries."
        );
    }

    [Fact]
    public void SavingALoadedSaveChangesNothing()
    {
        var first = SaveCompaction.Serialize(
            SaveCompaction.Event([
                new AtomEntry(Position(8) with { Rank = SnowRank.BeforeAll(2)[1] }, Id(80)),
                new AtomEntry(
                    Component(8) with
                    {
                        PrototypeRef = Tag<Prototype>(3, source: new Source(2)),
                    },
                    Id(90, source: new Source(2))
                ),
                new AtomEntry(
                    new Prototype { Id = Tag<Prototype>(3, source: new Source(2)), Name = "Card" },
                    Id(85, source: new Source(2))
                ),
            ])
        );

        // loading writes every atom in the save's event
        var loaded = Load(first);
        var second = SaveCompaction.Serialize(
            SaveCompaction.Event(loaded.Writes.Select(a => new AtomEntry(a, loaded.Id)))
        );

        Assert.Equal(first, second);
    }
}
