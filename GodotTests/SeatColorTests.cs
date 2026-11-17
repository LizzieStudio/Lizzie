using System.Collections.Generic;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class SeatColorTests
{
    // Seat colors come from Color.FromOkHsl, which needs the engine.
    [TestCase]
    [RequireGodotRuntime]
    public void ANewSeatTakesAColorNoOtherSeatHas()
    {
        var seats = new List<Seat>();
        for (int i = 0; i < 4; i++)
            seats.Add(new Seat { Id = new(i + 1), Color = Seat.NextColor(seats) });
        var removed = seats[1];
        seats.RemoveAt(1);

        AssertThat(Seat.NextColor(seats))
            .OverrideFailureMessage(
                "A new seat after a removal repeated another seat's color instead of taking the free one."
            )
            .IsEqual(removed.Color);
    }
}
