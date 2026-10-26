using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A store holds each atom by its id, keeps the lookups it has been read through,
/// and reports which query values each change touched.
/// </summary>
public class AtomStoreTests
{
    // components by their key, all in the empty stage
    private static readonly Identity<ComponentPosition, StageKey<ComponentPosition>> ById = new(s =>
        new(s.StageRef, s.Id)
    );

    private static readonly Lookup<ComponentPosition, SnowTag> ByContainer = new(s =>
        s.ContainerRef
    );

    // a lookup only components in a container are in
    private static readonly Lookup<ComponentPosition, SnowTag?> InContainer = new(s =>
        s.IsOnTable ? null : s.ContainerRef
    );

    private readonly Timeline _timeline = new();
    private readonly AtomStore<ComponentPosition, StageKey<ComponentPosition>> _store = new(ById);
    private long _clock;

    private static SnowTag Tag(int counter) => new(Snowport.HostSource, counter);

    private static StageKey<ComponentPosition> Key(int counter) =>
        new(Ref<Stage>.Empty, new(Tag(counter)));

    private static ComponentPosition Component(int id, int container = 0) =>
        new() { Id = new(Tag(id)), ContainerRef = container == 0 ? SnowTag.Empty : Tag(container) };

    /// <summary>Records an event writing <paramref name="atoms"/>, and merges it into the store.</summary>
    private TimelineEvent Write(params ComponentPosition[] atoms) =>
        Record(new TimelineEvent { Id = NextId(), Writes = atoms });

    /// <summary>Records an event unsetting the atom with <paramref name="id"/>, and merges it into the store.</summary>
    private TimelineEvent Unset(int id) =>
        Record(new TimelineEvent { Id = NextId(), Unsets = [Component(id)] });

    private ComponentPosition Value(int id) => (ComponentPosition)_store.Find(Key(id)).Value;

    private TimelineEvent Undo(TimelineEvent e) =>
        Record(
            new TimelineEvent
            {
                Id = NextId(),
                Undo = new UndoFlag { Reverses = e.Id },
            }
        );

    private SnowportId NextId() => new(++_clock, Snowport.HostSource);

    private TimelineEvent Record(TimelineEvent e)
    {
        _timeline.Add(e.Id, e);
        _store.Apply(_timeline, TimelineUndo.Changes(_timeline, e).ToList(), false);
        return e;
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
        var ids = atoms.Select(r => r.Id).Order().ToArray();
        var want = expected.Select(n => new Ref<Component>(Tag(n))).Order().ToArray();
        Assert.True(
            ids.SequenceEqual(want),
            $"Found the wrong atoms.\n\nExpected: {string.Join(", ", want)}\nFound:    {string.Join(", ", ids)}"
        );
    }

    [Fact]
    public void TheNewestWriteOfAnIdIsItsValue()
    {
        Write(Component(1, container: 5));
        Write(Component(1, container: 6));

        Assert.Equal(Tag(6), Value(1).ContainerRef);
    }

    [Fact]
    public void UndoingTheNewestWriteBringsBackTheOneBefore()
    {
        Write(Component(1, container: 5));
        var newer = Write(Component(1, container: 6));
        Undo(newer);

        Assert.Equal(Tag(5), Value(1).ContainerRef);
    }

    [Fact]
    public void UnsettingAnAtomRemovesIt()
    {
        Write(Component(1, container: 5));
        Unset(1);

        Assert.Null(Value(1));
    }

    [Fact]
    public void AnUnsetBeatsAWriteInTheSameEvent()
    {
        Write(Component(1, container: 5));
        Record(
            new TimelineEvent
            {
                Id = NextId(),
                Writes = [Component(1, container: 6)],
                Unsets = [Component(1)],
            }
        );

        Assert.Null(Value(1));
    }

    [Fact]
    public void ANewerWriteBringsAnUnsetAtomBack()
    {
        Write(Component(1, container: 5));
        Unset(1);
        Write(Component(1, container: 6));

        Assert.Equal(Tag(6), Value(1)?.ContainerRef);
    }

    [Fact]
    public void UndoingAnUnsetBringsTheAtomBack()
    {
        Write(Component(1, container: 5));
        Undo(Unset(1));

        Assert.Equal(Tag(5), Value(1)?.ContainerRef);
    }

    [Fact]
    public void UnsettingAnAtomNeverWrittenLeavesNothing()
    {
        Unset(1);

        Assert.Null(_store.Find(Key(1)).Value);
    }

    [Fact]
    public void ALookupReadLaterHoldsTheAtomsWrittenBefore()
    {
        Write(Component(1, container: 5), Component(2, container: 5), Component(3, container: 6));

        AssertIds(_store.Get(ByContainer, Tag(5)), 1, 2);
    }

    [Fact]
    public void AnAtomMovesBetweenLookupKeys()
    {
        Write(Component(1, container: 5));
        _store.Get(ByContainer, Tag(5));
        Write(Component(1, container: 6));

        AssertIds(_store.Get(ByContainer, Tag(5)));
        AssertIds(_store.Get(ByContainer, Tag(6)), 1);
    }

    [Fact]
    public void ANullLookupKeyLeavesTheAtomOutOfTheLookup()
    {
        Write(Component(1), Component(2, container: 5));

        AssertIds(_store.Get(InContainer, Tag(5)), 2);
        AssertIds(_store.Get(InContainer, null));
    }

    [Fact]
    public void LookupsAndCountsLeaveOutUnsetAtoms()
    {
        Write(Component(1, container: 5), Component(2, container: 5));
        Unset(2);

        Assert.Equal(1, _store.Count(ByContainer, Tag(5)));
        AssertIds(_store.Get(ByContainer, Tag(5)), 1);
    }

    [Fact]
    public void MovingAnAtomTouchesItsIdAndBothLookupKeysAndTheirCounts()
    {
        Write(Component(1, container: 5));
        _store.Get(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(() => Write(Component(1, container: 6))),
            new Touch(ById, Key(1), false),
            new Touch(ByContainer, Tag(5), false),
            new Touch(ByContainer, Tag(6), false),
            new Touch(ByContainer, Tag(5), true),
            new Touch(ByContainer, Tag(6), true)
        );
    }

    [Fact]
    public void ChangingAnAtomWithinItsLookupKeyLeavesTheCountAlone()
    {
        Write(Component(1, container: 5));
        _store.Get(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(() => Write(Component(1, container: 5) with { X = 10 })),
            new Touch(ById, Key(1), false),
            new Touch(ByContainer, Tag(5), false)
        );
    }

    [Fact]
    public void UnsettingAnAtomChangesItsLookupKeysCount()
    {
        Write(Component(1, container: 5));
        _store.Get(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(() => Unset(1)),
            new Touch(ById, Key(1), false),
            new Touch(ByContainer, Tag(5), false),
            new Touch(ByContainer, Tag(5), true)
        );
    }

    [Fact]
    public void AFirstWriteTouchesOnlyItsNewLookupKey()
    {
        _store.Get(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(() => Write(Component(1, container: 5))),
            new Touch(ById, Key(1), false),
            new Touch(ByContainer, Tag(5), false),
            new Touch(ByContainer, Tag(5), true)
        );
    }

    [Fact]
    public void LookupsNotReadYetAreNotTouched()
    {
        Write(Component(1, container: 5));

        AssertTouched(
            TouchesOf(() => Write(Component(1, container: 6))),
            new Touch(ById, Key(1), false)
        );
    }
}
