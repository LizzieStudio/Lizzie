using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// One player's actions, gestures and reversals, among another player's actions they can't undo.
/// <see cref="UndoTestBase.Actual"/> decides what each reversal writes, and after every step the player's
/// changes in effect must be the ones <see cref="TextbookUndo{TChange}"/> says.
/// </summary>
public abstract class TextbookTests : UndoTestBase
{
    [Fact]
    public void UndoMatchesTextbook() => Sample(UndoSessions.SinglePlayerUndos, MatchesTextbook);

    [Fact]
    public void UndoAndRedoMatchTextbook() => Sample(UndoSessions.SinglePlayer, MatchesTextbook);

    private void MatchesTextbook(Op[] ops)
    {
        const byte me = UndoSessions.Me;
        var session = new UndoSession(Actual);
        var textbook = new TextbookUndo<SnowportId>();

        for (int step = 0; step < ops.Length; step++)
        {
            var op = ops[step];
            var recorded = session.Apply(op);
            switch (op)
            {
                case Op.Act { Source: me }:
                    textbook.Change(recorded[0].Unit);
                    break;

                case Op.Gesture { Source: me }:
                    textbook.Change(recorded.First(e => e.Id == e.Group).Id);
                    break;

                case Op.Reversal reversal:
                    bool moved = reversal.Redo ? textbook.Redo() : textbook.Undo();
                    bool reversed = recorded.Count > 0;
                    if (moved != reversed)
                        Fail(
                            session,
                            step,
                            op,
                            $"the textbook {(moved ? "moves" : "stays put")}, "
                                + $"but {Actual.Name} {(reversed ? "wrote a reversal" : "wrote nothing")}"
                        );
                    break;
            }

            var inEffect = session
                .Log.Values.Where(e =>
                    e.Undo == null && e.Unit.source == me && !Actual.IsUndone(session.Log, e)
                )
                .Select(e => e.Unit)
                .ToHashSet();
            if (!inEffect.SetEquals(textbook.State))
                Fail(
                    session,
                    step,
                    op,
                    $"the textbook has [{Show(session, textbook.State)}] in effect, "
                        + $"{Actual.Name} has [{Show(session, inEffect)}]"
                );
        }
    }

    private static string Show(UndoSession session, IEnumerable<SnowportId> units) =>
        string.Join(", ", units.Order().Select(session.Name));

    private static void Fail(UndoSession session, int step, Op op, string message) =>
        Assert.Fail($"step {step}, {op}: {message}\nlog:\n{session.Dump()}");
}

/// <summary>The naive model against the textbook one, so each model checks the other.</summary>
public sealed class NaiveMatchesTextbook : TextbookTests
{
    protected override IUndoApi Actual { get; } = new NaiveUndo();
}

/// <summary>The game's implementation against the textbook model.</summary>
public sealed class CurrentMatchesTextbook : TextbookTests
{
    protected override IUndoApi Actual { get; } = new CurrentUndo();
}
