using System.Diagnostics;
using System.Linq;
using Xunit;

/// <summary>
/// Players pressing Undo or Redo at the same time, walked through by hand. A reversal that reverses a reversal
/// retires the other reversals on the same entry, so what they all reversed comes back in one step.
/// </summary>
public class SimultaneousUndoTests
{
    private const byte Me = 1;
    private static readonly TestScope Mine = new(ScopeKind.Mine, 0);
    private static readonly TestScope Others = new(ScopeKind.Others, 0);

    public static TheoryData<string> Apis => new() { "naive", "current" };

    private static IUndoApi Api(string name) =>
        name == "naive" ? new NaiveUndo() : new CurrentUndo();

    /// <summary>Asserts whether the session's first action, A, is in effect.</summary>
    private static void AssertLive(UndoSession session, IUndoApi api, bool live, string what)
    {
        var a = session.Log.GetAt(0).Value;
        Assert.True(
            api.IsUndone(session.Log, a) != live,
            $"{api.Name}: {what}\nlog:\n{session.Dump()}"
        );
    }

    /// <summary>
    /// I undo A, another player undoes it at the same time, and I Redo once I have theirs.
    /// <paramref name="after"/> 0 sorts theirs before mine, 1 after.
    /// </summary>
    [Theory]
    [InlineData("naive", 0)]
    [InlineData("naive", 1)]
    [InlineData("current", 0)]
    [InlineData("current", 1)]
    public void MyRedoRestoresWhatWeBothUndid(string name, int after)
    {
        var api = Api(name);
        var session = UndoSession.Play(
            [
                new Op.Act(Me, 1, 0, 0),
                new Op.Reversal(Me, false, Mine),
                new Op.Concurrent(2, false, Others, Back: 1, After: after),
            ],
            api
        );
        Assert.Equal(3, session.Log.Count);
        AssertLive(session, api, false, "both undos should have taken A away");

        var redo = Assert.Single(session.Apply(new Op.Reversal(Me, true, Mine))).Undo;
        var theirs = session.Log.Values.Single(e => e.Id.source == 2).Id;
        Assert.Equal([theirs], redo.Also);
        AssertLive(session, api, true, "one Redo should bring A back");
    }

    /// <summary>
    /// Theirs arrives after my Redo, sorted before it. A ends up undone, as though they undid it
    /// after my Redo, and Undo Others brings it back.
    /// </summary>
    [Theory]
    [MemberData(nameof(Apis))]
    public void LateUndoAfterMyRedoCanStillBeUndone(string name)
    {
        var api = Api(name);
        var session = UndoSession.Play(
            [
                new Op.Act(Me, 1, 0, 0),
                new Op.Reversal(Me, false, Mine),
                new Op.Reversal(Me, true, Mine),
                new Op.Concurrent(2, false, Others, Back: 2, After: 0),
            ],
            api
        );
        Assert.Equal(4, session.Log.Count);
        AssertLive(session, api, false, "their late undo should take A away");

        Assert.NotEmpty(session.Apply(new Op.Reversal(Me, false, Others)));
        AssertLive(session, api, true, "Undo Others should bring A back");
    }

    /// <summary>Two other players undo my action at the same time. One Undo Others brings it back.</summary>
    [Theory]
    [MemberData(nameof(Apis))]
    public void UndoOthersRestoresWhatTwoPlayersUndid(string name)
    {
        var api = Api(name);
        var session = UndoSession.Play(
            [
                new Op.Act(Me, 1, 0, 0),
                new Op.Reversal(2, false, Others),
                new Op.Concurrent(3, false, Others, Back: 1, After: 1),
            ],
            api
        );
        Assert.Equal(3, session.Log.Count);
        AssertLive(session, api, false, "both undos should have taken A away");

        Assert.NotEmpty(session.Apply(new Op.Reversal(Me, false, Others)));
        AssertLive(session, api, true, "one Undo Others should bring A back");
    }

    /// <summary>With nobody else pressing, reversals retire nothing, so they stay small in the log.</summary>
    [Theory]
    [MemberData(nameof(Apis))]
    public void ReversalsAloneRetireNothing(string name)
    {
        var session = UndoSession.Play(
            [
                new Op.Act(Me, 1, 0, 0),
                new Op.Reversal(Me, false, Mine),
                new Op.Reversal(Me, true, Mine),
                new Op.Act(Me, 2, 0, 0),
                new Op.Reversal(Me, false, Mine),
                new Op.Reversal(Me, false, Mine),
            ],
            Api(name)
        );
        Assert.All(session.Log.Values, e => Assert.Null(e.Undo?.Also));
    }
}
