using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CsCheck;
using Xunit;

/// <summary>
/// Laws any undo implementation must obey, whatever its rules, checked on multiplayer sessions where
/// no model says what should happen. <see cref="UndoTestBase.Actual"/> decides what every reversal writes.
/// </summary>
public abstract class UndoLawTests : UndoTestBase
{
    /// <summary>The scopes sessions are asked about.</summary>
    private static readonly TestScope[] Scopes =
    [
        new(ScopeKind.Mine, 0),
        new(ScopeKind.Others, 0),
        new(ScopeKind.Anyone, 0),
        new(ScopeKind.Anyone, 0b0101),
        new(ScopeKind.Mine, 0b0110),
        new(ScopeKind.Others, 0b1010),
    ];

    /// <summary>A session, then who presses at the end, in what scope, and how many times.</summary>
    private static readonly Gen<(Op[] Ops, byte Me, TestScope Scope, int Reversals)> Endings =
        Gen.Select(UndoSessions.All, Gen.Byte[1, 4], UndoSessions.Scope, Gen.Int[1, 5]);

    private static string Print((Op[] Ops, byte Me, TestScope Scope, int Reversals) ending) =>
        $"{UndoSessions.Print(ending.Ops)}\n  then s{ending.Me} in {ending.Scope}, "
        + $"{ending.Reversals} reversals";

    /// <summary>
    /// Every event of a gesture is undone together, or none is. Reversals no Undo or Redo would write
    /// are left out, since one can reverse part of a gesture.
    /// </summary>
    [Fact]
    public void GroupsAreAllOrNothing() =>
        Sample(
            UndoSessions.WellFormed,
            ops =>
            {
                var session = UndoSession.Play(ops, Actual);
                var groups = session
                    .Log.Values.Where(e => e.Undo == null && e.Group != SnowportId.Empty)
                    .GroupBy(e => e.Group);
                foreach (var group in groups)
                    if (group.Select(e => Actual.IsUndone(session.Log, e)).Distinct().Count() > 1)
                        Fail(session, $"{session.Name(group.Key)} is partly undone");
            }
        );

    /// <summary>No Undo or Redo lands on a gesture still in progress.</summary>
    [Fact]
    public void ReversalsSkipGesturesInProgress() =>
        Sample(
            UndoSessions.All,
            ops =>
            {
                var session = UndoSession.Play(ops, Actual);
                foreach (var me in UndoSession.Sources)
                foreach (var scope in Scopes)
                foreach (var (reversal, flag) in Reversals(session, me, scope))
                    if (
                        flag != null
                        && !NaiveUndo.IsClosed(
                            session.Log,
                            NaiveUndo.UnitOf(session.Log, flag.Reverses)
                        )
                    )
                        Fail(
                            session,
                            $"{reversal} by s{me} in {scope} would {session.Describe(flag)}, "
                                + "a gesture in progress"
                        );
            }
        );

    /// <summary>Undo and Redo in the player's own scope only ever change the player's own work.</summary>
    [Fact]
    public void MineOnlyTouchesMine() =>
        Sample(
            UndoSessions.All,
            ops =>
            {
                var session = UndoSession.Play(ops, Actual);
                foreach (var me in UndoSession.Sources)
                foreach (var mask in new[] { 0, 0b0101, 0b1010 })
                {
                    var scope = new TestScope(ScopeKind.Mine, mask);
                    foreach (var (reversal, flag) in Reversals(session, me, scope))
                        if (
                            flag != null
                            && NaiveUndo.UnitOf(session.Log, flag.Reverses).source != me
                        )
                            Fail(
                                session,
                                $"{reversal} by s{me} in {scope} would {session.Describe(flag)}, "
                                    + "which comes down to another player's work"
                            );
                }
            }
        );

    /// <summary>
    /// After a run of Undo reversals, each Redo puts everything back as it was before the matching Undo,
    /// outside the units the Undos redone so far changed. Inside those, each Redo may also have retired
    /// other reversals on the same entry, perhaps made at the same time as its Undo, so what the Undo
    /// reversed comes back whole (<see cref="ReversingAReversalRestoresWhatItReversed"/>).
    /// </summary>
    [Fact]
    public void RedoRetracesUndos() =>
        Sample(
            Endings,
            ending =>
            {
                var session = UndoSession.Play(ending.Ops, Actual);
                var before = new List<(HashSet<SnowportId> State, SnowportId Unit)>();
                for (int k = 0; k < ending.Reversals; k++)
                {
                    var state = InEffect(session);
                    var undo = session.Apply(new Op.Reversal(ending.Me, false, ending.Scope));
                    if (undo.Count == 0)
                        break;
                    before.Add((state, NaiveUndo.UnitOf(session.Log, undo[0].Undo.Reverses)));
                }

                var redone = new HashSet<SnowportId>();
                for (int k = before.Count - 1; k >= 0; k--)
                {
                    int redo = before.Count - k;
                    if (session.Apply(new Op.Reversal(ending.Me, true, ending.Scope)).Count == 0)
                        Fail(session, $"Redo {redo} of {before.Count} did nothing");
                    var (state, unit) = before[k];
                    redone.Add(unit);
                    if (
                        !Outside(session, InEffect(session), redone)
                            .SetEquals(Outside(session, state, redone))
                    )
                        Fail(
                            session,
                            $"Redo {redo} of {before.Count} didn't put things back as they were "
                                + $"outside {string.Join(", ", redone.Select(session.Name))}"
                        );
                }
            },
            Print
        );

    /// <summary>
    /// A reversal that reverses another reversal puts back what that reversal reversed, even when other reversals,
    /// perhaps made at the same time by other players, reversed it too. So a Redo always brings back
    /// what its Undo took away, and an Undo of an undo always restores what it undid.
    /// </summary>
    [Fact]
    public void ReversingAReversalRestoresWhatItReversed() =>
        Sample(
            Gen.OneOf(UndoSessions.All, UndoSessions.Simultaneous),
            ops =>
            {
                var session = new UndoSession(Actual);
                foreach (var op in ops)
                {
                    var recorded = session.Apply(op);
                    // Only a reversal decided on the whole log; one that missed some can't retire them.
                    if (op is not Op.Reversal || recorded.Count == 0)
                        continue;

                    var flag = recorded[0].Undo;
                    if (
                        session.Log.TryGetValue(flag.Reverses, out var target)
                        && target.Undo is { } reversed
                        && reversed.Reverses.CompareTo(target.Id) < 0
                        && session.Log.TryGetValue(reversed.Reverses, out var restored)
                        && (restored.Undo != null || restored.Unit == restored.Id)
                        && Actual.IsUndone(session.Log, restored)
                    )
                        Fail(
                            session,
                            $"{op} wrote a reversal to {session.Describe(flag)}, "
                                + $"but {session.Name(restored.Id)} is still undone"
                        );
                }
            }
        );

    /// <summary>A new action of the player's own leaves nothing to redo in scopes that take it.</summary>
    [Fact]
    public void NewActionLeavesNothingToRedo() =>
        Sample(
            Endings,
            ending =>
            {
                var session = UndoSession.Play(ending.Ops, Actual);
                // Finish any gesture of theirs first, so the action stands on its own.
                session.Apply(new Op.End(ending.Me, 0));
                session.Apply(new Op.Act(ending.Me, 1, 0, 0));
                foreach (
                    var scope in new TestScope[]
                    {
                        new(ScopeKind.Mine, 0),
                        new(ScopeKind.Anyone, 0),
                    }
                )
                    if (Actual.Redo(session.Log, ending.Me, scope.For(ending.Me)) is { } flag)
                        Fail(
                            session,
                            $"Redo by s{ending.Me} in {scope} would still {session.Describe(flag)}"
                        );
            },
            Print
        );

    /// <summary>What Undo and Redo would each write now.</summary>
    private (string Reversal, UndoFlag Flag)[] Reversals(
        UndoSession session,
        byte me,
        TestScope scope
    ) =>
        [
            ("Undo", Actual.Undo(session.Log, me, scope.For(me))),
            ("Redo", Actual.Redo(session.Log, me, scope.For(me))),
        ];

    /// <summary>The actions in effect.</summary>
    private HashSet<SnowportId> InEffect(UndoSession session) =>
        session
            .Log.Values.Where(e => e.Undo == null && !Actual.IsUndone(session.Log, e))
            .Select(e => e.Id)
            .ToHashSet();

    /// <summary>The actions of <paramref name="state"/> outside <paramref name="units"/>.</summary>
    private static HashSet<SnowportId> Outside(
        UndoSession session,
        HashSet<SnowportId> state,
        HashSet<SnowportId> units
    ) => state.Where(id => !units.Contains(session.Log[id].Unit)).ToHashSet();

    private static void Fail(UndoSession session, string message) =>
        Assert.Fail($"{message}\nlog:\n{session.Dump()}");
}

/// <summary>The naive model obeys the laws, so its rules are coherent.</summary>
public sealed class NaiveObeysLaws : UndoLawTests
{
    protected override IUndoApi Actual { get; } = new NaiveUndo();
}

/// <summary>The game's implementation obeys the laws.</summary>
public sealed class CurrentObeysLaws : UndoLawTests
{
    protected override IUndoApi Actual { get; } = new CurrentUndo();
}
