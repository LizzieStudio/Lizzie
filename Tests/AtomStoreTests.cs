using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lizzie.Replication.Machinery;
using Xunit;

/// <summary>
/// A store holds each atom by its id, keeps the groups it has been read through,
/// and reports which query values each change touched.
/// </summary>
public class AtomStoreTests
{
    private static readonly Group<ComponentState, SnowTag> ByContainer = new(s => s.ContainerRef);

    // a group only components in a container are in
    private static readonly Group<ComponentState, SnowTag?> InContainer = new(s =>
        s.IsOnTable ? null : s.ContainerRef
    );

    private readonly EventLog _log = new();
    private readonly AtomStore<ComponentState, SnowTag> _store = new(ComponentState.ById);
    private long _clock;

    private static SnowTag Tag(int counter) => new(Snowport.HostSource, counter);

    private static ComponentState Component(int id, int container = 0, bool deleted = false) =>
        new()
        {
            Id = Tag(id),
            ContainerRef = container == 0 ? SnowTag.Empty : Tag(container),
            Deleted = deleted,
        };

    /// <summary>Records an event writing <paramref name="atoms"/>, and merges it into the store.</summary>
    private TableEvent Write(params ComponentState[] atoms) =>
        Record(new TableEvent { Id = NextId(), Records = atoms });

    private TableEvent Undo(TableEvent e) =>
        Record(
            new TableEvent
            {
                Id = NextId(),
                Undo = new UndoFlag { Reverses = e.Id },
            }
        );

    private SnowportId NextId() => new(++_clock, Snowport.HostSource);

    private TableEvent Record(TableEvent e)
    {
        _log.Add(e.Id, e);
        _store.Apply(_log, UndoLog.Changes(_log, e).ToList(), false);
        return e;
    }

    /// <summary>The touches the next write reports.</summary>
    private Touch[] TouchesOf(params ComponentState[] atoms)
    {
        var changes = new List<RecordChange>();
        _store.Changed += changes.AddRange;
        Write(atoms);
        _store.Changed -= changes.AddRange;
        return _store.Touched(changes).ToArray();
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
    private static void AssertIds(IEnumerable<ComponentState> atoms, params int[] expected)
    {
        var ids = atoms.Select(r => r.Id).Order().ToArray();
        var want = expected.Select(Tag).Order().ToArray();
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

        Assert.Equal(Tag(6), _store.Find(Tag(1)).ContainerRef);
    }

    [Fact]
    public void UndoingTheNewestWriteBringsBackTheOneBefore()
    {
        Write(Component(1, container: 5));
        var newer = Write(Component(1, container: 6));
        Undo(newer);

        Assert.Equal(Tag(5), _store.Find(Tag(1)).ContainerRef);
    }

    [Fact]
    public void AGroupReadLaterHoldsTheAtomsWrittenBefore()
    {
        Write(Component(1, container: 5), Component(2, container: 5), Component(3, container: 6));

        AssertIds(_store.InGroup(ByContainer, Tag(5)), 1, 2);
    }

    [Fact]
    public void AnAtomMovesBetweenGroupKeys()
    {
        Write(Component(1, container: 5));
        _store.InGroup(ByContainer, Tag(5));
        Write(Component(1, container: 6));

        AssertIds(_store.InGroup(ByContainer, Tag(5)));
        AssertIds(_store.InGroup(ByContainer, Tag(6)), 1);
    }

    [Fact]
    public void ANullGroupKeyPutsTheAtomInNoGroup()
    {
        Write(Component(1), Component(2, container: 5));

        AssertIds(_store.InGroup(InContainer, Tag(5)), 2);
        AssertIds(_store.InGroup(InContainer, null));
    }

    [Fact]
    public void CountsLeaveOutDeletedAtomsButGroupsHoldThem()
    {
        Write(Component(1, container: 5), Component(2, container: 5, deleted: true));

        Assert.Equal(1, _store.Count(ByContainer, Tag(5)));
        AssertIds(_store.InGroup(ByContainer, Tag(5)), 1, 2);
    }

    [Fact]
    public void MovingAnAtomTouchesItsIdAndBothGroupKeysAndTheirCounts()
    {
        Write(Component(1, container: 5));
        _store.InGroup(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(Component(1, container: 6)),
            new Touch(ComponentState.ById, Tag(1), false),
            new Touch(ByContainer, Tag(5), false),
            new Touch(ByContainer, Tag(6), false),
            new Touch(ByContainer, Tag(5), true),
            new Touch(ByContainer, Tag(6), true)
        );
    }

    [Fact]
    public void ChangingAnAtomWithinItsGroupKeyLeavesTheCountAlone()
    {
        Write(Component(1, container: 5));
        _store.InGroup(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(Component(1, container: 5) with { X = 10 }),
            new Touch(ComponentState.ById, Tag(1), false),
            new Touch(ByContainer, Tag(5), false)
        );
    }

    [Fact]
    public void DeletingAnAtomChangesItsGroupKeysCount()
    {
        Write(Component(1, container: 5));
        _store.InGroup(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(Component(1, container: 5, deleted: true)),
            new Touch(ComponentState.ById, Tag(1), false),
            new Touch(ByContainer, Tag(5), false),
            new Touch(ByContainer, Tag(5), true)
        );
    }

    [Fact]
    public void AFirstWriteTouchesOnlyItsNewGroupKey()
    {
        _store.InGroup(ByContainer, Tag(5));

        AssertTouched(
            TouchesOf(Component(1, container: 5)),
            new Touch(ComponentState.ById, Tag(1), false),
            new Touch(ByContainer, Tag(5), false),
            new Touch(ByContainer, Tag(5), true)
        );
    }

    [Fact]
    public void GroupsNotReadYetAreNotTouched()
    {
        Write(Component(1, container: 5));

        AssertTouched(
            TouchesOf(Component(1, container: 6)),
            new Touch(ComponentState.ById, Tag(1), false)
        );
    }
}
