using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CsCheck;
using Xunit;

/// <summary>
/// Model-based properties. Random sessions of actions, gestures, reversals, departures and odd flags
/// are played out, with <see cref="Expected"/> deciding what each reversal writes,
/// and <see cref="Actual"/> must give the same answer to every question about the log.
/// </summary>
/// <remarks>
/// A failure prints the shrunk operations, the question that got different answers, and the log.
/// Rerun one exactly by passing the printed seed to <c>Sample</c>.
/// </remarks>
public abstract class UndoModelTests : UndoTestBase
{
    protected abstract IUndoApi Expected { get; }

    /// <summary>The scopes every finished session is asked about.</summary>
    private static readonly TestScope[] Scopes =
    [
        new(ScopeKind.Mine, 0),
        new(ScopeKind.Others, 0),
        new(ScopeKind.Anyone, 0),
        new(ScopeKind.Anyone, 0b0101),
        new(ScopeKind.Mine, 0b0110),
        new(ScopeKind.Others, 0b1010),
    ];

    #region Undo and redo

    [Fact]
    public void UndoPicksTheSameTarget() =>
        Sample(
            UndoSessions.All,
            ops =>
            {
                var session = UndoSession.Play(
                    ops,
                    Expected,
                    (s, op) =>
                    {
                        if (op is Op.Reversal { Redo: false } reversal)
                            SameUndo(s, reversal.Source, reversal.Scope);
                    }
                );
                foreach (var me in UndoSession.Sources)
                foreach (var scope in Scopes)
                    SameUndo(session, me, scope);
            }
        );

    [Fact]
    public void RedoPicksTheSameTarget() =>
        Sample(
            UndoSessions.All,
            ops =>
            {
                var session = UndoSession.Play(
                    ops,
                    Expected,
                    (s, op) =>
                    {
                        if (op is Op.Reversal { Redo: true } reversal)
                            SameRedo(s, reversal.Source, reversal.Scope);
                    }
                );
                foreach (var me in UndoSession.Sources)
                foreach (var scope in Scopes)
                    SameRedo(session, me, scope);
            }
        );

    [Fact]
    public void EmptyLogHasNothingToUndo()
    {
        Gen.Select(Gen.Byte[0, 3], UndoSessions.Scope)
            .Sample(query =>
            {
                var (me, scope) = query;
                var session = UndoSession.Play([], Expected);
                SameUndo(session, me, scope);
                SameRedo(session, me, scope);
            });
    }

    #endregion

    #region In effect

    [Fact]
    public void SameEventsAreInEffect() =>
        Sample(UndoSessions.All, ops => SameInEffect(UndoSession.Play(ops, Expected)));

    [Fact]
    public void NothingIsUndoneWithoutFlags() =>
        Sample(UndoSessions.WithoutFlags, ops => SameInEffect(UndoSession.Play(ops, Expected)));

    [Fact]
    public void SameChanges() =>
        Sample(
            UndoSessions.All,
            ops =>
            {
                var session = UndoSession.Play(ops, Expected);
                foreach (var e in session.Log.Values)
                    Same(
                        session,
                        $"What {session.Name(e.Id)} may have changed",
                        Show(session, Expected.Changes(session.Log, e).OrderBy(c => c.Id)),
                        Show(session, Actual.Changes(session.Log, e).OrderBy(c => c.Id))
                    );
            }
        );

    #endregion

    #region Groups

    [Fact]
    public void SameGroupsAreOpen() =>
        Sample(
            UndoSessions.All,
            ops =>
            {
                var session = UndoSession.Play(
                    ops,
                    Expected,
                    (s, op) =>
                    {
                        if (op is Op.Leave leave)
                            SameOpenGroups(s, leave.Source);
                    }
                );
                foreach (var source in UndoSession.Sources)
                    SameOpenGroups(session, source);
            }
        );

    [Fact]
    public void UngroupedSessionsHaveNoOpenGroups() =>
        Sample(
            UndoSessions.Ungrouped,
            ops =>
            {
                var session = UndoSession.Play(ops, Expected);
                foreach (var source in UndoSession.Sources)
                    SameOpenGroups(session, source);
            }
        );

    #endregion

    #region Helpers

    private void SameInEffect(UndoSession s) =>
        Same(
            s,
            "Events in effect, newest first",
            Show(s, Expected.InEffect(s.Log)),
            Show(s, Actual.InEffect(s.Log))
        );

    private void SameUndo(UndoSession s, byte me, TestScope scope) =>
        Same(
            s,
            $"Undo by s{me} in {scope}",
            s.Describe(Expected.Undo(s.Log, me, scope.For(me))),
            s.Describe(Actual.Undo(s.Log, me, scope.For(me)))
        );

    private void SameRedo(UndoSession s, byte me, TestScope scope) =>
        Same(
            s,
            $"Redo by s{me} in {scope}",
            s.Describe(Expected.Redo(s.Log, me, scope.For(me))),
            s.Describe(Actual.Redo(s.Log, me, scope.For(me)))
        );

    private void SameOpenGroups(UndoSession s, byte source) =>
        Same(
            s,
            $"Open groups of s{source}",
            Show(s, Expected.OpenGroups(s.Log, source)),
            Show(s, Actual.OpenGroups(s.Log, source))
        );

    private void Same<T>(UndoSession session, string question, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Assert.Fail(
                $"{question}\n  {Expected.Name}: {expected}\n  {Actual.Name}: {actual}\nlog:\n{session.Dump()}"
            );
    }

    private static string Show(UndoSession s, List<SnowportId> ids) =>
        ids == null ? "null" : "[" + string.Join(", ", ids.Order().Select(s.Name)) + "]";

    private static string Show(UndoSession s, IEnumerable<TableEvent> events) =>
        events == null ? "null" : "[" + string.Join(", ", events.Select(e => s.Name(e.Id))) + "]";

    #endregion
}

/// <summary>The game's implementation against the naive model.</summary>
public sealed class CurrentMatchesNaive : UndoModelTests
{
    protected override IUndoApi Actual { get; } = new CurrentUndo();
    protected override IUndoApi Expected { get; } = new NaiveUndo();
}
