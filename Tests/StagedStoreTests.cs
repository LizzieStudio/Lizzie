using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A staged store shows each stage's own writes, and its parent's where it has none,
/// and reports which query values each change to what a stage shows touched.
/// </summary>
public class StagedStoreTests
{
    private readonly Timeline _timeline = new();
    private readonly AtomStore<Stage, Ref<Stage>> _stages = new(Stage.ById);
    private readonly StagedStore _store;
    private bool _bulkLoading;
    private long _clock;

    // whether the stores show removed positions
    private bool _showsRemoved;

    public StagedStoreTests() => _store = new(_stages, () => _showsRemoved);

    private static SnowTag Tag(int counter) => new(Source.Host, counter);

    private static Ref<T> Tag<T>(int counter)
        where T : Atom => new(Tag(counter));

    private static PositionKey Key(int stage, int id) => new(Tag<Stage>(stage), Tag<Component>(id));

    private static Stage StageOf(int id, int parent = 0) =>
        new()
        {
            Id = Tag<Stage>(id),
            ParentRef = parent == 0 ? Ref<Stage>.Empty : Tag<Stage>(parent),
        };

    private static ComponentPosition PositionOf(int stage, int id, int x = 0, int inside = 0) =>
        new()
        {
            StageRef = Tag<Stage>(stage),
            Id = Tag<Component>(id),
            X = x,
            Location = inside == 0 ? null : new InComponent(Tag<Component>(inside)),
        };

    private static ComponentPosition RemovalOf(int stage, int id) =>
        PositionOf(stage, id).Removal();

    private void ShowRemoved(bool shown)
    {
        _showsRemoved = shown;
        _store.ShowsRemovedChanged();
        _store.Settle();
    }

    /// <summary>Records an event writing <paramref name="atoms"/>, and merges it into the stores.</summary>
    private TimelineEvent Write(params Atom[] atoms) =>
        Record(new TimelineEvent { Id = NextId(), Writes = atoms });

    private static readonly IAtomReader R = new ListReader();

    /// <summary>Records an event unsetting <paramref name="atoms"/>, and merges it into the stores.</summary>
    private TimelineEvent Unset(params Atom[] atoms) =>
        Record(new TimelineEvent { Id = NextId(), Unsets = atoms.Select(R.Unset).ToArray() });

    private TimelineEvent Undo(TimelineEvent e) =>
        Record(
            new TimelineEvent
            {
                Id = NextId(),
                Undo = new UndoFlag { Reverses = e.Id },
            }
        );

    /// <summary>Records the first event of a transition, drafting <paramref name="position"/>, and returns the transition.</summary>
    private Transition Open(ComponentPosition position)
    {
        var transition = new Transition(NextId());
        Record(
            new TimelineEvent
            {
                Id = transition.Start,
                Writes = [position with { Transitions = [transition] }],
            }
        );
        return transition;
    }

    private ComponentPosition Value(int stage, int id) =>
        (ComponentPosition)_store.Find(Key(stage, id)).Value;

    private SnowStamp NextId() => new(++_clock, Source.Host);

    private TimelineEvent Record(TimelineEvent e)
    {
        _timeline.Add(e.Id, e);
        var changed = TimelineUndo.Changes(_timeline, e).ToList();
        _store.Apply(_timeline, changed, _bulkLoading);
        _stages.Apply(_timeline, changed, _bulkLoading);
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
    private static void AssertIds(IEnumerable<ComponentPosition> atoms, params int[] expected)
    {
        var ids = atoms.Select(p => p.Id).Order().ToArray();
        var want = expected.Select(n => Tag<Component>(n)).Order().ToArray();
        Assert.True(
            ids.SequenceEqual(want),
            $"Found the wrong atoms.\n\nExpected: {string.Join(", ", want)}\nFound:    {string.Join(", ", ids)}"
        );
    }

    [StackTraceHidden]
    private void AssertShows(int stage, int id, int? x, bool removed = false)
    {
        var entry = _store.Find(Key(stage, id));
        var shown = (ComponentPosition)entry.Value;
        bool matches = shown?.X == x && entry.Removed == removed;
        if (shown != null)
            matches &= shown.StageRef == Tag<Stage>(stage) && !shown.Removed;
        Assert.True(
            matches,
            $"Stage {stage} shows the wrong position of {id}.\n\nExpected: {Describe(x, removed, Tag<Stage>(stage))}\nShown:    {(shown == null ? "nothing" : Describe(shown.X, entry.Removed, shown.StageRef))}"
        );
    }

    private static string Describe(int? x, bool removed, Ref<Stage> stage) =>
        x == null ? "nothing" : $"X = {x} in stage {stage}{(removed ? ", removed" : "")}";

    [Fact]
    public void AStageShowsItsParentsWrite()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));

        AssertShows(2, 10, 5);
    }

    [Fact]
    public void AnInheritedAtomHasTheVersionOfTheWriteItCameFrom()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        var source = Write(PositionOf(1, 10, x: 5));

        Assert.Equal(source.Id, _store.Find(Key(2, 10)).LastUpdate);
    }

    [Fact]
    public void AStagesOwnWriteShadowsItsParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));
        Write(PositionOf(2, 10, x: 7));

        AssertShows(1, 10, 5);
        AssertShows(2, 10, 7);
    }

    [Fact]
    public void UnsettingAnOwnWriteRevealsTheParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));
        Write(PositionOf(2, 10, x: 7));
        Unset(PositionOf(2, 10));

        AssertShows(2, 10, 5);
    }

    [Fact]
    public void UnsettingWhatAStageWroteRevealsItsParentsAndLeavesItsInheritedAtoms()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5), PositionOf(1, 12, x: 6));
        Write(PositionOf(2, 10, x: 7), PositionOf(2, 11, x: 8));
        Record(
            new TimelineEvent
            {
                Id = NextId(),
                Unsets = [R.UnsetAll(ComponentPosition.WrittenIn, Tag<Stage>(2))],
            }
        );

        AssertShows(2, 10, 5);
        AssertShows(2, 11, null);
        AssertShows(2, 12, 6);
    }

    /// <summary>Resetting a table resets what's being dragged too, until the reset is undone.</summary>
    [Fact]
    public void UnsettingWhatAStageWroteTakesItsDraftsOutOfTheirTransition()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));
        Open(PositionOf(2, 10, x: 7));
        var reset = Record(
            new TimelineEvent
            {
                Id = NextId(),
                Unsets = [R.UnsetAll(ComponentPosition.WrittenIn, Tag<Stage>(2))],
            }
        );
        AssertShows(2, 10, 5);

        Undo(reset);

        AssertShows(2, 10, 7);
    }

    [Fact]
    public void AStagedTypeCanOnlyBeUnsetByWhatAStageWrote() =>
        Assert.Throws<System.ArgumentException>(() =>
            R.UnsetAll(ComponentPosition.ByStage, Tag<Stage>(2))
        );

    [Fact]
    public void AGrandparentsWriteReachesTheGrandchild()
    {
        Write(StageOf(1), StageOf(2, parent: 1), StageOf(3, parent: 2));
        Write(PositionOf(1, 10, x: 5));

        AssertShows(3, 10, 5);
    }

    [Fact]
    public void AStoreMadeAfterItsStagesShowsThem()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        var late = new StagedStore(_stages, () => _showsRemoved);

        var e = new TimelineEvent { Id = NextId(), Writes = [PositionOf(1, 10, x: 5)] };
        _timeline.Add(e.Id, e);
        late.Apply(_timeline, [e], bulkLoading: false);
        late.Settle();

        Assert.Equal(5, ((ComponentPosition)late.Find(Key(2, 10)).Value)?.X);
    }

    [Fact]
    public void AWriteInAStageAndItsStageInOneEventShows()
    {
        Write(StageOf(1), PositionOf(1, 10, x: 5));

        AssertShows(1, 10, 5);
    }

    [Fact]
    public void AStageThatIsNotWrittenShowsNothing()
    {
        Write(PositionOf(1, 10, x: 5));

        AssertShows(1, 10, null);
    }

    [Fact]
    public void ChangingAStagesParentChangesWhatItShows()
    {
        Write(StageOf(1), StageOf(2, parent: 1), StageOf(3));
        Write(PositionOf(1, 10, x: 5), PositionOf(3, 10, x: 7), PositionOf(1, 11));
        Write(StageOf(2, parent: 3));

        AssertShows(2, 10, 7);
        AssertShows(2, 11, null);
    }

    [Fact]
    public void ARemovedStagesAtomsDisappear()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5), PositionOf(2, 11));
        AssertIds(_store.Get(ComponentPosition.ByStage, Tag<Stage>(2)), 10, 11);

        AssertTouched(
            TouchesOf(() => Unset(StageOf(2))),
            new Touch(ComponentPosition.ById, Key(2, 10), false),
            new Touch(ComponentPosition.ById, Key(2, 11), false),
            new Touch(ComponentPosition.ByStage, Tag<Stage>(2), false),
            new Touch(ComponentPosition.ByStage, Tag<Stage>(2), true)
        );
        AssertShows(2, 10, null);
        AssertShows(2, 11, null);
        AssertIds(_store.Get(ComponentPosition.ByStage, Tag<Stage>(2)));
        AssertShows(1, 10, 5);
    }

    [Fact]
    public void UndoingAStagesRemovalBringsBackItsAtoms()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5), PositionOf(2, 11, x: 7));
        var newGame = Record(
            new TimelineEvent
            {
                Id = NextId(),
                Writes = [StageOf(3, parent: 1)],
                Unsets = [R.Unset(StageOf(2, parent: 1))],
            }
        );
        AssertShows(2, 11, null);

        Undo(newGame);

        AssertShows(2, 10, 5);
        AssertShows(2, 11, 7);
        AssertIds(_store.Get(ComponentPosition.ByStage, Tag<Stage>(2)), 10, 11);
    }

    [Fact]
    public void AParentsWriteTouchesEveryStageThatShowsIt()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));

        AssertTouched(
            TouchesOf(() => Write(PositionOf(1, 10, x: 6))),
            new Touch(ComponentPosition.ById, Key(1, 10), false),
            new Touch(ComponentPosition.ById, Key(2, 10), false)
        );
    }

    [Fact]
    public void AParentsWriteUnderAnOwnWriteTouchesOnlyTheParent()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5), PositionOf(2, 10, x: 7));

        AssertTouched(
            TouchesOf(() => Write(PositionOf(1, 10, x: 6))),
            new Touch(ComponentPosition.ById, Key(1, 10), false)
        );
    }

    [Fact]
    public void ALookupListsWhatEachStageShowsAndWrittenListsOnlyItsWrites()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10), PositionOf(2, 11));

        AssertIds(_store.Get(ComponentPosition.ByStage, Tag<Stage>(2)), 10, 11);
        AssertIds(_store.Get(ComponentPosition.WrittenIn, Tag<Stage>(2)), 11);
        Assert.Equal(1, _store.Count(ComponentPosition.WrittenIn, Tag<Stage>(2)));
    }

    [Fact]
    public void AWriteTouchesTheWrittenLookupOfItsStageOnly()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        _store.Get(ComponentPosition.WrittenIn, Tag<Stage>(1));
        _store.Get(ComponentPosition.WrittenIn, Tag<Stage>(2));

        AssertTouched(
            TouchesOf(() => Write(PositionOf(1, 10))),
            new Touch(ComponentPosition.ById, Key(1, 10), false),
            new Touch(ComponentPosition.ById, Key(2, 10), false),
            new Touch(ComponentPosition.WrittenIn, Tag<Stage>(1), false),
            new Touch(ComponentPosition.WrittenIn, Tag<Stage>(1), true)
        );
    }

    [Fact]
    public void SavedAtomsAreTheWritesOnly()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        var written = PositionOf(1, 10, x: 5);
        Write(written);

        Assert.Equal<Atom>([written], _store.SavedAtoms(_timeline).Select(e => e.Value));
    }

    [Fact]
    public void ALoopOfParentsAndWhatsUnderItShowNothing()
    {
        Write(StageOf(1, parent: 2));
        Write(StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5), PositionOf(2, 11, x: 7));
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
        Write(PositionOf(1, 10, x: 5), PositionOf(2, 11, x: 7));

        Write(StageOf(1));

        AssertShows(1, 10, 5);
        AssertShows(2, 10, 5);
        AssertShows(2, 11, 7);
    }

    [Fact]
    public void AStageUnderADeletedOneShowsNothing()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5), PositionOf(2, 11, x: 7));

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
            Write(PositionOf(1, 10, x: 5), PositionOf(2, 11, x: 7));
        });
        Assert.Empty(touches);
        AssertShows(2, 10, null);

        AssertTouched(
            TouchesOf(FlushBulkLoad),
            new Touch(ComponentPosition.ById, Key(1, 10), false),
            new Touch(ComponentPosition.ById, Key(2, 10), false),
            new Touch(ComponentPosition.ById, Key(2, 11), false)
        );
        AssertShows(2, 10, 5);
        AssertShows(2, 11, 7);
    }

    [Fact]
    public void UndoingAnOwnWriteRevealsTheParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));
        Undo(Write(PositionOf(2, 10, x: 7)));

        AssertShows(2, 10, 5);
    }

    [Fact]
    public void CancelingATransitionRevealsTheParents()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));
        var open = Open(PositionOf(2, 10, x: 7));
        AssertShows(2, 10, 7);

        Record(new TimelineEvent { Id = NextId(), Ends = open });

        AssertShows(2, 10, 5);
    }

    [Fact]
    public void ARemovalHidesTheParentsAtomInTheStageAndBelow()
    {
        Write(StageOf(1), StageOf(2, parent: 1), StageOf(3, parent: 2));
        Write(PositionOf(1, 10, x: 5));
        Write(RemovalOf(2, 10));

        AssertShows(1, 10, 5);
        AssertShows(2, 10, null);
        AssertShows(3, 10, null);
        AssertIds(_store.Get(ComponentPosition.ByStage, Tag<Stage>(2)));
    }

    [Fact]
    public void ARemovalHoldsOnlyItsId() =>
        Assert.Equal(
            new ComponentPosition
            {
                StageRef = Tag<Stage>(2),
                Id = Tag<Component>(10),
                Removed = true,
            },
            PositionOf(2, 10, x: 5, inside: 11).Removal()
        );

    [Fact]
    public void WritingARemovedAtomBringsItBack()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));
        Write(RemovalOf(2, 10));

        Write(PositionOf(2, 10, x: 7));

        AssertShows(2, 10, 7);
    }

    [Fact]
    public void ARemovedAtomShowsAsTheNearestWriteAboveItWhileRemovedAtomsAreShown()
    {
        Write(StageOf(1), StageOf(2, parent: 1), StageOf(3, parent: 2));
        Write(PositionOf(1, 10, x: 5), PositionOf(2, 10, x: 6));
        Write(RemovalOf(3, 10));

        ShowRemoved(true);

        AssertShows(3, 10, 6, removed: true);
        AssertShows(2, 10, 6);
    }

    [Fact]
    public void AnAtomRemovedWithNothingAboveShowsNothingEvenWhileRemovedAtomsAreShown()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(2, 10, x: 5));
        Write(RemovalOf(2, 10));

        ShowRemoved(true);

        AssertShows(2, 10, null);
    }

    [Fact]
    public void HidingRemovedAtomsAgainHidesThem()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 10, x: 5));
        Write(RemovalOf(2, 10));
        ShowRemoved(true);

        AssertTouched(
            TouchesOf(() => ShowRemoved(false)),
            new Touch(ComponentPosition.ById, Key(2, 10), false)
        );
        AssertShows(2, 10, null);
    }

    [Fact]
    public void WhatsInsideARemovedComponentGoesWithIt()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 20), PositionOf(1, 21, x: 5, inside: 20));
        _store.Get(ComponentPosition.ByStage, Tag<Stage>(2));

        AssertTouched(
            TouchesOf(() => Write(RemovalOf(2, 20))),
            new Touch(ComponentPosition.ById, Key(2, 20), false),
            new Touch(ComponentPosition.ById, Key(2, 21), false),
            new Touch(ComponentPosition.ByStage, Tag<Stage>(2), false),
            new Touch(ComponentPosition.ByStage, Tag<Stage>(2), true)
        );
        AssertShows(2, 21, null);
        AssertShows(1, 21, 5);
    }

    [Fact]
    public void WhatsInsideARemovedComponentComesBackWithIt()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 20), PositionOf(1, 21, x: 5, inside: 20));
        var removal = Write(RemovalOf(2, 20));

        Undo(removal);

        AssertShows(2, 21, 5);
    }

    [Fact]
    public void WhatsInsideAComponentAStageWroteStaysWhenAParentRemovesIt()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 20), PositionOf(1, 21, x: 5, inside: 20));
        Write(PositionOf(2, 20, x: 3));

        Write(RemovalOf(1, 20));

        AssertShows(1, 21, null);
        AssertShows(2, 20, 3);
        AssertShows(2, 21, 5);
    }

    [Fact]
    public void WhatsInsideAShownRemovedComponentIsRemovedToo()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(
            PositionOf(1, 20),
            PositionOf(1, 21, x: 5, inside: 20),
            PositionOf(1, 22, inside: 21)
        );
        Write(RemovalOf(2, 20));

        ShowRemoved(true);

        AssertShows(2, 20, 0, removed: true);
        AssertShows(2, 21, 5, removed: true);
        AssertShows(2, 22, 0, removed: true);
    }

    [Fact]
    public void TakingAnAtomOutOfARemovedComponentBringsItBack()
    {
        Write(StageOf(1), StageOf(2, parent: 1));
        Write(PositionOf(1, 20), PositionOf(1, 21, x: 5, inside: 20));
        Write(RemovalOf(2, 20));
        ShowRemoved(true);

        Write(PositionOf(2, 21, x: 7));

        AssertShows(2, 20, 0, removed: true);
        AssertShows(2, 21, 7);
    }

    [Fact]
    public void AnAtomInsideAComponentNoStageShowsShowsNothing()
    {
        Write(StageOf(1));
        Write(PositionOf(1, 21, x: 5, inside: 20));

        AssertShows(1, 21, null);
    }
}
