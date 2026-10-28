using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Seats keep their names, hands and players wherever they're moved, and new ones go anywhere.
/// </summary>
public class SeatTests
{
    private static readonly SnowportId Stamp = new((1000L << 8) | 1);

    // Seats 1 to n, in order.
    private static List<Seat> SeatsOf(int n)
    {
        SnowRank rank = default;
        return Enumerable
            .Range(1, n)
            .Select(i => new Seat
            {
                Id = new(i),
                Rank = rank = rank.After(new SnowportId(((long)i << 8) | 1)),
            })
            .ToList();
    }

    // A seat put at index among seats.
    private static Seat InsertedAt(List<Seat> seats, int index) =>
        new() { Id = new(9), Rank = SnowRank.At(seats, index, Stamp) };

    [Fact]
    public void ASeatCanGoBeforeTheOthers()
    {
        var seats = SeatsOf(3);
        var R = new ListReader([.. seats, InsertedAt(seats, 0)]);

        Assert.Equal([9, 1, 2, 3], R.Get(Seat.Live).Select(s => (int)s.Id.Tag));
    }

    [Fact]
    public void AnUnnamedSeatGoesByItsPlace()
    {
        var seats = SeatsOf(2);
        seats[1] = seats[1] with { Name = "North" };
        var R = new ListReader([.. seats, InsertedAt(seats, 0)]);

        Assert.Equal(["Seat 1", "Seat 2", "North"], R.Get(Seat.Live).Select((s, i) => s.Label(i)));
    }

    [Fact]
    public void ARemovedSeatIsNotListed()
    {
        var seats = SeatsOf(2);
        var R = new ListReader(seats[0] with { Deleted = true }, seats[1]);

        Assert.Equal([2], R.Get(Seat.Live).Select(s => (int)s.Id.Tag));
    }

    [Fact]
    public void APlayerInARemovedSeatWatches()
    {
        var seats = SeatsOf(2);
        var R = new ListReader(
            seats[0] with
            {
                Deleted = true,
            },
            seats[1],
            new Seating
            {
                Seats = ImmutableDictionary<byte, Ref<Seat>>
                    .Empty.Add(2, seats[0].Id)
                    .Add(3, seats[1].Id),
            }
        );

        Assert.Equal(Ref<Seat>.Empty, R.SeatOf(2));
        Assert.Equal(seats[1].Id, R.SeatOf(3));
    }
}
