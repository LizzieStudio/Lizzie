using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lizzie.Replication.Machinery;
using Xunit;

/// <summary>
/// A staged store shows each stage's own writes, and its parent's where it has none,
/// and reports which query values each change to what a stage shows touched.
/// </summary>
public class StagedStoreTests
{
    public sealed record Piece : StagedAtom
    {
        public static readonly StagedId<Piece> ById = new(p => p.Id);
        public static readonly Group<Piece, SnowTag> ByStage = new(p => p.Stage);
        public static readonly Written<Piece> WrittenIn = new();

        public required SnowTag Id { get; init; }
        public int X { get; init; }
    }

    private readonly EventLog _log = new();
    private readonly AtomStore<Stage, SnowTag> _stages = new(Stage.ById);
    private readonly StagedStore<Piece> _store;
    private bool _bulkLoading;
    private long _clock;

    public StagedStoreTests() => _store = new(Piece.ById, _stages);

    private static SnowTag Tag(int counter) => new(Snowport.HostSource, counter);

    private static StageKey Key(int stage, int id) => new(Tag(stage), Tag(id));

    private static Stage StageOf(int id, int parent = 0) =>
        new() { Id = Tag(id), ParentRef = parent == 0 ? SnowTag.Empty : Tag(parent) };

    private static Piece PieceOf(int stage, int id, int x = 0, bool removed = false) =>
        new()
        {
            Stage = Tag(stage),
            Id = Tag(id),
            X = x,
            Removed = removed,
        };

    /// <summary>Records an event writing <paramref name="atoms"/>, and merges it into the stores.</summary>
    private TableEvent Write(params Atom[] atoms) =>
        Record(new TableEvent { Id = NextId(), Writes = atoms });

    /// <summary>Records an event unsetting <paramref name="atoms"/>, and merges it into the stores.</summary>
    private TableEvent Unset(params Atom[] atoms) =>
        Record(new TableEvent { Id = NextId(), Unsets = atoms });

    private TableEvent Undo(TableEvent e) =>
        Record(
            new TableEvent
            {
                Id = NextId(),
                Undo = new UndoFlag { Reverses = e.Id },
            }
        );

    private Piece Value(int stage, int id) => (Piece)_store.Find(Key(stage, id)).Value;

    private SnowportId NextId() => new(++_clock, Snowport.HostSource);

    // Merges the piece store first, so the stage store hasn't seen the event yet, as can happen in AtomService.
    private TableEvent Record(TableEvent e)
    {
        _log.Add(e.Id, e);
        var changed = UndoLog.Changes(_log, e).ToList();
        _store.Apply(_log, changed, _bulkLoading);
        _stages.Apply(_log, changed, _bulkLoading);
        _store.Settle();
        return e;
    }

    private void FlushBulkLoad()
    {
        _bulkLoading = false;
        _store.FlushBulkLoad();
        _stages.FlushBulkLoad();
        _store.Settle();
    }

    /// <summary>The touches <paramref name="change"/> reports.</summary>
    private Touch[] TouchesOf(System.Action change)
    {
        var touches = new List<Touch>();
        _store.Changed += touches.AddRange;
        change();
        _store.Changed -= touches.AddRange;
        return touches.ToArray();
    }

    [StackTraceHidden]
    private static void AssertTouched(Touch[] touched, params Touch[] expected)
    {
        Assert.True(
            touched.ToHashSet().SetEquals(expected),
            $"The change touched the wrong query values.\n\nExpected: {string.Join(", ", expected)}\nTouched:  {string.Join(", ", touched)}"
        );
    }

    [StackTraceHidden]
    private static void AssertIds(IEnumerable<Piece> atoms, params int[] expected)
    {
        var ids = atoms.Select(p => p.Id).Order().ToArray();
        var want = expected.Select(Tag).Order().ToArray();
        Assert.True(
            ids.SequenceEqual(want),
            $"Found the wrong atoms.\n\nExpected: {string.Join(", ", want)}\nFound:    {string.Join(", ", ids)}"
        );
    }

    [StackTraceHidden]
    private void AssertShows(int stage, int id, int? x)
    {
        var shown = Value(stage, id);
        Assert.True(
            shown?.X == x && (shown == null || shown.Stage == Tag(stage)),
            $"Stage {stage} shows the wrong piece {id}.\n\nExpected: {(x == null ? "nothing" : $"X = {x} in stage {stage}")}\nShown:    {(shown == null ? "nothing" : $"X = {shown.X} in stage {shown.Stage}")}"
        );
    }

    [Fact]
    public void AStageShowsItsParentsWrite()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5));

        AssertShows(2, 10, 5);
    }

    [Fact]
    public void AnInheritedAtomHasTheVersionOfTheWriteItCameFrom()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        var source = Write(PieceOf(1, 10, x: 5));

        Assert.Equal(source.Id, _store.Find(Key(2, 10)).LastUpdate);
    }

    [Fact]
    public void AStagesOwnWriteShadowsItsParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5));
        Write(PieceOf(2, 10, x: 7));

        AssertShows(1, 10, 5);
        AssertShows(2, 10, 7);
    }

    [Fact]
    public void ATombstoneHidesTheParentsAtomInTheStageAndBelow()
    {
        Write(StageOf(1), StageOf(2, parent: 1), StageOf(3, parent: 2));
        Write(PieceOf(1, 10, x: 5));
        Write(PieceOf(2, 10, removed: true));

        AssertShows(1, 10, 5);
        AssertShows(2, 10, null);
        AssertShows(3, 10, null);
    }

    [Fact]
    public void UnsettingAnOwnWriteRevealsTheParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5));
        Write(PieceOf(2, 10, x: 7));
        Unset(PieceOf(2, 10));

        AssertShows(2, 10, 5);
    }

    [Fact]
    public void AGrandparentsWriteReachesTheGrandchild()
    {
        Write(StageOf(1), StageOf(2, parent: 1), StageOf(3, parent: 2));
        Write(PieceOf(1, 10, x: 5));

        AssertShows(3, 10, 5);
    }

    [Fact]
    public void AStoreMadeAfterItsStagesShowsThem()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        var late = new StagedStore<Piece>(Piece.ById, _stages);

        var e = new TableEvent { Id = NextId(), Writes = [PieceOf(1, 10, x: 5)] };
        _log.Add(e.Id, e);
        late.Apply(_log, [e], bulkLoading: false);
        late.Settle();

        Assert.Equal(5, ((Piece)late.Find(Key(2, 10)).Value)?.X);
    }

    [Fact]
    public void AWriteInAStageAndItsStageInOneEventShows()
    {
        Write(StageOf(1), PieceOf(1, 10, x: 5));

        AssertShows(1, 10, 5);
    }

    [Fact]
    public void AStageThatIsNotWrittenShowsNothing()
    {
        Write(PieceOf(1, 10, x: 5));

        AssertShows(1, 10, null);
    }

    [Fact]
    public void ChangingAStagesParentChangesWhatItShows()
    {
        Write(StageOf(1), StageOf(2, parent: 1), StageOf(3));
        Write(PieceOf(1, 10, x: 5), PieceOf(3, 10, x: 7), PieceOf(1, 11));
        Write(StageOf(2, parent: 3));

        AssertShows(2, 10, 7);
        AssertShows(2, 11, null);
    }

    [Fact]
    public void ARemovedStagesAtomsDisappear()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5), PieceOf(2, 11));
        AssertIds(_store.InGroup(Piece.ByStage, Tag(2)), 10, 11);

        AssertTouched(
            TouchesOf(() => Unset(StageOf(2))),
            new Touch(Piece.ById, Key(2, 10), false),
            new Touch(Piece.ById, Key(2, 11), false),
            new Touch(Piece.ByStage, Tag(2), false),
            new Touch(Piece.ByStage, Tag(2), true)
        );
        AssertShows(2, 10, null);
        AssertShows(2, 11, null);
        AssertIds(_store.InGroup(Piece.ByStage, Tag(2)));
        AssertShows(1, 10, 5);
    }

    [Fact]
    public void UndoingAStagesRemovalBringsBackItsAtoms()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5), PieceOf(2, 11, x: 7));
        var newGame = Record(
            new TableEvent
            {
                Id = NextId(),
                Writes = [StageOf(3, parent: 1)],
                Unsets = [StageOf(2, parent: 1)],
            }
        );
        AssertShows(2, 11, null);

        Undo(newGame);

        AssertShows(2, 10, 5);
        AssertShows(2, 11, 7);
        AssertIds(_store.InGroup(Piece.ByStage, Tag(2)), 10, 11);
    }

    [Fact]
    public void AParentsWriteTouchesEveryStageThatShowsIt()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5));

        AssertTouched(
            TouchesOf(() => Write(PieceOf(1, 10, x: 6))),
            new Touch(Piece.ById, Key(1, 10), false),
            new Touch(Piece.ById, Key(2, 10), false)
        );
    }

    [Fact]
    public void AParentsWriteUnderAnOwnWriteTouchesOnlyTheParent()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5), PieceOf(2, 10, x: 7));

        AssertTouched(
            TouchesOf(() => Write(PieceOf(1, 10, x: 6))),
            new Touch(Piece.ById, Key(1, 10), false)
        );
    }

    [Fact]
    public void AGroupListsWhatEachStageShowsAndWrittenListsOnlyItsWrites()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10), PieceOf(2, 11));

        AssertIds(_store.InGroup(Piece.ByStage, Tag(2)), 10, 11);
        AssertIds(_store.InGroup(Piece.WrittenIn, Tag(2)), 11);
        Assert.Equal(1, _store.Count(Piece.WrittenIn, Tag(2)));
    }

    [Fact]
    public void AWriteTouchesTheWrittenGroupOfItsStageOnly()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        _store.InGroup(Piece.WrittenIn, Tag(1));
        _store.InGroup(Piece.WrittenIn, Tag(2));

        AssertTouched(
            TouchesOf(() => Write(PieceOf(1, 10))),
            new Touch(Piece.ById, Key(1, 10), false),
            new Touch(Piece.ById, Key(2, 10), false),
            new Touch(Piece.WrittenIn, Tag(1), false),
            new Touch(Piece.WrittenIn, Tag(1), true)
        );
    }

    [Fact]
    public void SavedAtomsAreTheWritesOnly()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        var written = PieceOf(1, 10, x: 5);
        Write(written);

        Assert.Equal<Atom>([written], _store.SavedAtoms());
    }

    [Fact]
    public void ATombstoneOverItsParentsAtomIsSaved()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        var tombstone = PieceOf(2, 10, removed: true);
        Write(PieceOf(1, 10, x: 5), tombstone);

        Assert.Contains(tombstone, _store.SavedAtoms());
    }

    [Fact]
    public void ATombstoneOverNothingIsNotSaved()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, removed: true), PieceOf(2, 11, removed: true));

        Assert.Empty(_store.SavedAtoms());
    }

    [Fact]
    public void ALoopOfParentsAndWhatsUnderItShowNothing()
    {
        Write(StageOf(1, parent: 2));
        Write(StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5), PieceOf(2, 11, x: 7));
        Write(StageOf(3, parent: 1));

        AssertShows(1, 10, null);
        AssertShows(2, 11, null);
        AssertShows(3, 10, null);
    }

    [Fact]
    public void BreakingALoopShowsItsStagesAgain()
    {
        Write(StageOf(1, parent: 2));
        Write(StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5), PieceOf(2, 11, x: 7));

        Write(StageOf(1));

        AssertShows(1, 10, 5);
        AssertShows(2, 10, 5);
        AssertShows(2, 11, 7);
    }

    [Fact]
    public void AStageUnderADeletedOneShowsNothing()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5), PieceOf(2, 11, x: 7));

        Write(StageOf(1) with { Deleted = true });

        AssertShows(1, 10, null);
        AssertShows(2, 11, null);
    }

    [Fact]
    public void ABulkLoadShowsNothingUntilItsFlush()
    {
        _bulkLoading = true;
        var touches = TouchesOf(() =>
        {
            Write(StageOf(1), StageOf(2, parent: 1));
            Write(PieceOf(1, 10, x: 5), PieceOf(2, 11, x: 7));
        });
        Assert.Empty(touches);
        AssertShows(2, 10, null);

        AssertTouched(
            TouchesOf(FlushBulkLoad),
            new Touch(Piece.ById, Key(1, 10), false),
            new Touch(Piece.ById, Key(2, 10), false),
            new Touch(Piece.ById, Key(2, 11), false)
        );
        AssertShows(2, 10, 5);
        AssertShows(2, 11, 7);
    }

    [Fact]
    public void UndoingAnOwnWriteRevealsTheParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5));
        Undo(Write(PieceOf(2, 10, x: 7)));

        AssertShows(2, 10, 5);
    }

    [Fact]
    public void CancelingAGroupRevealsTheParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PieceOf(1, 10, x: 5));
        var open = NextId();
        Record(
            new TableEvent
            {
                Id = open,
                Group = open,
                Writes = [PieceOf(2, 10, x: 7)],
            }
        );
        AssertShows(2, 10, 7);

        Record(
            new TableEvent
            {
                Id = NextId(),
                Group = open,
                End = GroupEnd.Canceled,
            }
        );

        AssertShows(2, 10, 5);
    }
}
