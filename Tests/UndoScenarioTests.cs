using System;
using System.Linq;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Undo and redo walked through by hand, one reversal at a time.
/// </summary>
public class UndoScenarioTests
{
    private const byte Admin = Snowport.AdminSource;
    private const byte Me = 1;
    private const byte Other = 2;

    private static bool Mine(byte author, Atom _) => author == Me;

    private static bool Others(byte author, Atom _) => author != Me;

    private readonly EventLog _log = new();
    private long _clock;

    private SnowportId NextId(byte source) => new(++_clock, source);

    /// <summary>
    /// Records an action that writes <paramref name="atom"/>, in <paramref name="group"/> if given,
    /// and closing it if <paramref name="close"/>.
    /// </summary>
    private SnowportId Act(
        byte source,
        int atom = 1,
        SnowportId group = default,
        bool close = false
    )
    {
        var id = NextId(source);
        _log.Add(
            id,
            new TableEvent
            {
                Id = id,
                Group = group,
                End = close ? GroupEnd.Closed : GroupEnd.None,
                Writes = [new Prototype { Id = atom }],
            }
        );
        return id;
    }

    /// <summary>Records an event that only closes <paramref name="group"/>.</summary>
    private void CloseGroup(SnowportId group, byte source)
    {
        var id = NextId(source);
        _log.Add(
            id,
            new TableEvent
            {
                Id = id,
                Group = group,
                End = GroupEnd.Closed,
            }
        );
    }

    /// <summary>Records an event that cancels <paramref name="group"/>.</summary>
    private TableEvent CancelGroup(SnowportId group, byte source)
    {
        var id = NextId(source);
        var e = new TableEvent
        {
            Id = id,
            Group = group,
            End = GroupEnd.Canceled,
        };
        _log.Add(id, e);
        return e;
    }

    /// <summary>Records a reversal directly, as the host does, as an admin event, for a player who left.</summary>
    private TableEvent AddFlag(SnowportId reverses, byte source)
    {
        var id = NextId(source);
        var e = new TableEvent
        {
            Id = id,
            Undo = new UndoFlag { Reverses = reverses },
        };
        _log.Add(id, e);
        return e;
    }

    /// <summary>Records the first action of a group, which names it.</summary>
    private SnowportId StartGroup(byte source, int atom = 1)
    {
        var id = Act(source, atom);
        _log[id].Group = id;
        return id;
    }

    private UndoFlag Undo(Func<byte, Atom, bool> scope, byte source = Me) =>
        Reversal(UndoLog.Undo(_log, source, scope), source);

    private UndoFlag Redo(Func<byte, Atom, bool> scope, byte source = Me) =>
        Reversal(UndoLog.Redo(_log, source, scope), source);

    private UndoFlag Reversal(UndoFlag flag, byte source)
    {
        if (flag != null)
        {
            var id = NextId(source);
            _log.Add(id, new TableEvent { Id = id, Undo = flag });
        }
        return flag;
    }

    private bool IsUndone(SnowportId id) => !UndoLog.InEffect(_log).Contains(_log[id]);

    /// <summary>The ids of the events <paramref name="e"/> may have changed, oldest first.</summary>
    private SnowportId[] Changes(TableEvent e) =>
        UndoLog.Changes(_log, e).Select(c => c.Id).ToArray();

    /// <summary>The unit a reversal changes, which its chain of reversals comes down to.</summary>
    private SnowportId Changed(UndoFlag flag) => NaiveUndo.UnitOf(_log, flag.Reverses);

    [Fact]
    public void UndoWalksBackThroughOwnActions()
    {
        var a = Act(Me);
        var b = Act(Me);

        Assert.Equal(b, Changed(Undo(Mine)));
        Assert.Equal(a, Changed(Undo(Mine)));
        Assert.Null(Undo(Mine));
        Assert.True(IsUndone(a));
        Assert.True(IsUndone(b));
    }

    [Fact]
    public void UndoUnfoldsPastANewAction()
    {
        var a = Act(Me);
        Undo(Mine);
        var b = Act(Me);

        // History is A, undo A, B, and each reversal walks back one step of it.
        Assert.Equal(b, Changed(Undo(Mine)));
        Assert.True(IsUndone(b));

        Assert.Equal(a, Changed(Undo(Mine)));
        Assert.False(IsUndone(a));

        Assert.Equal(a, Changed(Undo(Mine)));
        Assert.True(IsUndone(a));

        Assert.Null(Undo(Mine));
    }

    [Fact]
    public void RedoReversesUndosInTurn()
    {
        var a = Act(Me);
        var b = Act(Me);
        Undo(Mine);
        Undo(Mine);

        Assert.Equal(a, Changed(Redo(Mine)));
        Assert.Equal(b, Changed(Redo(Mine)));
        Assert.Null(Redo(Mine));
        Assert.False(IsUndone(a));
        Assert.False(IsUndone(b));
    }

    [Fact]
    public void RedoReversesAnUnfoldingUndo()
    {
        var a = Act(Me);
        Undo(Mine);
        var b = Act(Me);
        Undo(Mine); // undoes B
        Undo(Mine); // restores A

        Assert.Equal(a, Changed(Redo(Mine)));
        Assert.True(IsUndone(a));

        Assert.Equal(b, Changed(Redo(Mine)));
        Assert.False(IsUndone(b));

        Assert.Null(Redo(Mine));
        Assert.True(IsUndone(a));
        Assert.False(IsUndone(b));
    }

    /// <summary>
    /// Redo retraces the undos in reverse, even when unfolding flagged the same move more than once.
    /// </summary>
    [Fact]
    public void RedoRetracesAnUnfoldedHistory()
    {
        var a = Act(Me); // create at A
        var b = Act(Me); // move to B
        Undo(Mine); // back to A
        var c = Act(Me); // move to C
        Assert.Equal(c, Position());

        Undo(Mine);
        Assert.Equal(a, Position());
        Undo(Mine);
        Assert.Equal(b, Position());
        Undo(Mine);
        Assert.Equal(a, Position());

        Redo(Mine);
        Assert.Equal(b, Position());
        Redo(Mine);
        Assert.Equal(a, Position());
        Redo(Mine);
        Assert.Equal(c, Position());
        Assert.Null(Redo(Mine));
    }

    /// <summary>
    /// After redoing everything, undoing walks the same history again, unfolding included.
    /// </summary>
    [Fact]
    public void UndoUnfoldsAgainAfterRedo()
    {
        var a = Act(Me); // create at A
        var b = Act(Me); // move to B
        Undo(Mine); // back to A
        var c = Act(Me); // move to C

        Undo(Mine); // A
        Undo(Mine); // B
        Undo(Mine); // A
        Redo(Mine); // B
        Redo(Mine); // A
        Redo(Mine); // C
        Assert.Equal(c, Position());

        Undo(Mine);
        Assert.Equal(a, Position());
        Undo(Mine);
        Assert.Equal(b, Position());
        Undo(Mine);
        Assert.Equal(a, Position());
    }

    /// <summary>
    /// An undo that was redone cancels out with its redo, so a new action has nothing to unfold.
    /// </summary>
    [Fact]
    public void RedoneUndoLeavesNoHistory()
    {
        var a = Act(Me); // create at A
        var b = Act(Me); // move to B
        Undo(Mine); // back to A
        Redo(Mine); // B again
        var c = Act(Me); // move to C

        Undo(Mine);
        Assert.Equal(b, Position());
        Undo(Mine);
        Assert.Equal(a, Position());
        Undo(Mine);
        Assert.Equal(SnowportId.Empty, Position());
        Assert.Null(Undo(Mine));
    }

    /// <summary>
    /// A new action unfolds only the undos that weren't redone.
    /// </summary>
    [Fact]
    public void OnlyUndosLeftUndoneUnfold()
    {
        var a = Act(Me); // create at A
        var b = Act(Me); // move to B
        Undo(Mine); // back to A
        Undo(Mine); // gone
        Redo(Mine); // A again
        var c = Act(Me); // move to C
        Assert.Equal(c, Position());

        Undo(Mine);
        Assert.Equal(a, Position());
        Undo(Mine);
        Assert.Equal(b, Position());
        Undo(Mine);
        Assert.Equal(a, Position());
        Undo(Mine);
        Assert.Equal(SnowportId.Empty, Position());
        Assert.Null(Undo(Mine));
    }

    /// <summary>Where the component is: the newest action still in effect, or Empty if none is.</summary>
    private SnowportId Position() =>
        _log.Values.LastOrDefault(e => e.Undo == null && !IsUndone(e.Id))?.Id ?? SnowportId.Empty;

    [Fact]
    public void NewActionClearsRedo()
    {
        Act(Me);
        Undo(Mine);
        Act(Me);

        Assert.Null(Redo(Mine));
    }

    [Fact]
    public void GroupUndoesTogether()
    {
        var g = StartGroup(Me, atom: 1);
        var second = Act(Me, atom: 2, group: g);
        var third = Act(Me, atom: 3, group: g, close: true);

        Assert.Equal(g, Changed(Undo(Mine)));
        Assert.True(IsUndone(g));
        Assert.True(IsUndone(second));
        Assert.True(IsUndone(third));
    }

    [Fact]
    public void FlagCoversOnlyOlderEvents()
    {
        // A gesture is flagged while it's still going.
        var g = StartGroup(Other, atom: 1);
        var before = Act(Other, atom: 2, group: g);
        var flag = AddFlag(g, Admin);
        var after = Act(Other, atom: 3, group: g);

        Assert.True(IsUndone(g));
        Assert.True(IsUndone(before));
        Assert.False(IsUndone(after));
        Assert.Equal([g, before], Changes(flag));
    }

    [Fact]
    public void StreamsKeepToTheirAuthors()
    {
        var mine = Act(Me);
        var theirs = Act(Other);

        Assert.Equal(mine, Changed(Undo(Mine)));
        Assert.Equal(theirs, Changed(Undo(Others)));
    }

    [Fact]
    public void AnotherPlayersUndoIsTheirAction()
    {
        var theirs = Act(Other);
        Undo((author, _) => author == Other, source: Other);
        Assert.True(IsUndone(theirs));

        // Undoing their undo restores what they undid.
        Assert.Equal(theirs, Changed(Undo(Others)));
        Assert.False(IsUndone(theirs));
    }

    [Fact]
    public void UndoingOthersStaysInTheirStream()
    {
        var theirs = Act(Other);
        var mine = Act(Me);
        Undo(Others);

        // Undoing their action isn't one of mine, so it neither blocks nor joins my history.
        Assert.Equal(mine, Changed(Undo(Mine)));
        Assert.Equal(theirs, Changed(Redo(Others)));
        Assert.False(IsUndone(theirs));
    }

    [Fact]
    public void FlagOnAnUnknownTargetDoesNothing()
    {
        var a = Act(Me);
        var missing = new SnowportId(999, Me);
        var id = NextId(Me);
        _log.Add(
            id,
            new TableEvent
            {
                Id = id,
                Undo = new UndoFlag { Reverses = missing },
            }
        );

        Assert.Empty(Changes(_log[id]));
        Assert.False(IsUndone(a));
        Assert.Equal(a, Changed(UndoLog.Undo(_log, Me, Mine)));
    }

    [Fact]
    public void GestureInProgressIsPassedOver()
    {
        var mine = Act(Me);
        Act(Other);
        StartGroup(Me, atom: 2); // my drag, still going

        // Neither my undo nor another player's lands on the drag.
        Assert.Equal(mine, Changed(UndoLog.Undo(_log, Me, Mine)));
        Assert.Equal(mine, Changed(UndoLog.Undo(_log, Other, (author, _) => author != Other)));
    }

    [Fact]
    public void ClosingEventFinishesTheGesture()
    {
        var g = StartGroup(Me, atom: 1);
        Act(Me, atom: 2, group: g);
        Assert.Null(UndoLog.Undo(_log, Me, Mine));

        // Closed by someone else, the gesture is still the player's own.
        CloseGroup(g, Other);
        Assert.Equal(g, Changed(Undo(Mine)));
    }

    /// <summary>
    /// A gesture closed by someone else sits in history where it finished,
    /// so Undo Others reaches it before anything older.
    /// </summary>
    [Fact]
    public void GestureClosedByAnotherIsUndoneFromWhereItFinished()
    {
        var g = StartGroup(Other, atom: 1);
        Act(Other, atom: 2);
        CloseGroup(g, Me);

        Assert.Equal(g, Changed(Undo(Others)));
    }

    [Fact]
    public void FlagOnAGestureInProgressIsPassedOver()
    {
        var g = StartGroup(Other);
        AddFlag(g, Admin);

        Assert.Null(UndoLog.Undo(_log, Me, Others));
    }

    [Fact]
    public void GestureInProgressDoesNotBlockRedo()
    {
        var a = Act(Me);
        Undo(Mine);
        StartGroup(Me, atom: 2);

        Assert.Equal(a, Changed(Redo(Mine)));
    }

    [Fact]
    public void AbandonedGestureIsCanceled()
    {
        var open = StartGroup(Other, atom: 1);
        Act(Other, atom: 2, group: open);
        var finished = StartGroup(Other, atom: 3);
        Act(Other, atom: 4, group: finished, close: true);
        StartGroup(Me, atom: 5);

        Assert.Equal([open], UndoLog.OpenGroups(_log, Other));

        // What the host, me here, writes when the player leaves.
        CancelGroup(open, Me);

        Assert.Empty(UndoLog.OpenGroups(_log, Other));
        Assert.True(IsUndone(open));
        Assert.False(IsUndone(finished));
    }

    [Fact]
    public void CanceledGestureNeverHappened()
    {
        var g = StartGroup(Me, atom: 1);
        var move = Act(Me, atom: 2, group: g);
        Assert.True(UndoLog.IsOpen(_log, g));

        var cancel = CancelGroup(g, Me);

        Assert.False(UndoLog.IsOpen(_log, g));
        Assert.True(IsUndone(g));
        Assert.True(IsUndone(move));
        Assert.True(IsUndone(cancel.Id));
        // What the gesture wrote goes back, so its atoms are recomputed.
        Assert.Equal([g, move, cancel.Id], Changes(cancel));
    }

    [Fact]
    public void UndoPassesOverACanceledGesture()
    {
        var a = Act(Me);
        var g = StartGroup(Me, atom: 2);
        CancelGroup(g, Me);

        Assert.Equal(a, Changed(Undo(Mine)));
        Assert.Null(Undo(Mine));
    }

    /// <summary>A canceled gesture isn't a new action, so the undo before it is still there to redo.</summary>
    [Fact]
    public void CanceledGestureLeavesRedo()
    {
        var a = Act(Me);
        Undo(Mine);
        var g = StartGroup(Me, atom: 2);
        CancelGroup(g, Me);

        Assert.Equal(a, Changed(Redo(Mine)));
        Assert.Null(Redo(Mine));
    }

    /// <summary>A canceled gesture isn't a new action either, so undo doesn't start unfolding past it.</summary>
    [Fact]
    public void CanceledGestureDoesNotUnfold()
    {
        var a = Act(Me);
        Undo(Mine);
        var g = StartGroup(Me, atom: 2);
        CancelGroup(g, Me);

        Assert.Null(Undo(Mine));
        Assert.True(IsUndone(a));
    }

    [Fact]
    public void CancelAfterCloseDoesNothing()
    {
        var g = StartGroup(Me, atom: 1);
        Act(Me, atom: 2, group: g, close: true);
        CancelGroup(g, Me);

        Assert.False(IsUndone(g));
        Assert.Equal(g, Changed(Undo(Mine)));
    }

    [Fact]
    public void CancelKeepsOthersLaterWrites()
    {
        var g = StartGroup(Me, atom: 1);
        var theirs = Act(Other, atom: 1);
        CancelGroup(g, Me);

        Assert.True(IsUndone(g));
        Assert.False(IsUndone(theirs));
    }

    /// <summary>
    /// History is the only truth: two reversals that reverse the same action both have to be reversed
    /// before it's back. Undo and Redo reverse both at once (see SimultaneousUndoTests); these are
    /// written by hand, one at a time.
    /// </summary>
    [Fact]
    public void TwoUndosOfOneActionNeedBothReversed()
    {
        var x = Act(Me);
        var mine = AddFlag(x, Me);
        var theirs = AddFlag(x, Other);
        Assert.True(IsUndone(x));

        AddFlag(mine.Id, Me);
        Assert.True(IsUndone(x));

        AddFlag(theirs.Id, Other);
        Assert.False(IsUndone(x));
    }

    /// <summary>
    /// My reversal that reverses another player's undo is my undo of their action, so it walks in the
    /// Others stream, even though what comes back is my own work.
    /// </summary>
    [Fact]
    public void RedoOthersRedoesMyUndoOfTheirUndo()
    {
        var mine = Act(Me);
        Undo((author, _) => true, source: Other); // they undo my action
        Assert.True(IsUndone(mine));

        Undo(Others); // I undo their undo
        Assert.False(IsUndone(mine));

        Assert.NotNull(Redo(Others));
        Assert.True(IsUndone(mine));
    }

    /// <summary>
    /// Another player's undo that they redid is gone from history for everyone,
    /// so Undo Others passes over it.
    /// </summary>
    [Fact]
    public void UndoOthersPassesTheirRedoneUndo()
    {
        var first = Act(Other);
        var second = Act(Other);
        Undo((author, _) => author == Other, source: Other);
        Redo((author, _) => author == Other, source: Other);

        Assert.Equal(second, Changed(Undo(Others)));
        Assert.True(IsUndone(second));

        Assert.Equal(first, Changed(Undo(Others)));
        Assert.True(IsUndone(first));
    }

    /// <summary>
    /// A loaded save is one admin event holding the whole table, so no undo reaches it,
    /// whoever presses and in whatever scope.
    /// </summary>
    [Fact]
    public void LoadedSaveIsNeverUndone()
    {
        var save = Act(Admin, atom: 1);

        Assert.Null(Undo(Mine));
        Assert.Null(Undo(Others));
        Assert.Null(Undo((_, _) => true, source: Other));

        // Walking back through my own history stops before it.
        var mine = Act(Me, atom: 2);
        Assert.Equal(mine, Changed(Undo(Mine)));
        Assert.Null(Undo(Mine));
        Assert.False(IsUndone(save));
    }

    /// <summary>Undo This on an atom no one has changed since the save loaded has nothing to undo.</summary>
    [Fact]
    public void UntouchedAtomHasNothingToUndo()
    {
        Act(Admin, atom: 1);

        Assert.Null(Undo((_, r) => r is Prototype p && p.Id == 1));
    }

    /// <summary>An unset is in scope when what it unsets is, so Undo This brings an unset atom back.</summary>
    [Fact]
    public void UnsetIsInTheScopeOfWhatItUnsets()
    {
        Act(Admin, atom: 1);
        var id = NextId(Me);
        _log.Add(id, new TableEvent { Id = id, Unsets = [new Prototype { Id = 1 }] });

        Assert.Equal(id, Changed(Undo((_, r) => r is Prototype p && p.Id == 1)));
    }

    /// <summary>The host's bookkeeping, like giving a seat its hand, doesn't take away a player's redo.</summary>
    [Fact]
    public void AdminEventDoesNotBlockRedo()
    {
        var a = Act(Me);
        Undo(Mine);
        Act(Admin, atom: 2);

        Assert.NotNull(Redo((_, _) => true));
        Assert.False(IsUndone(a));
    }
}
