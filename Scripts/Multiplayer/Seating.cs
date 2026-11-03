using System.Collections.Immutable;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Which <see cref="Seat"/> each player is sitting in, by their <see cref="Source"/>.
///
/// <para>
/// Only the host or a solo player writes it, as admin events, so no one can undo a seat change.
/// <see cref="PresenceSynchronizer"/> takes seat requests and decides between players asking for the same seat.
/// A player without a seat has no hand and can't drag, whether they chose No Seat or haven't chosen yet.
/// </para>
/// </summary>
[NotSaved]
[JsonName("Seating")]
public sealed record Seating : Atom
{
    public static readonly Singleton<Seating> Only = new();

    /// <summary>
    /// The seat of each seated player. Players without a seat are missing.
    /// </summary>
    public ImmutableDictionary<Source, Ref<Seat>> Seats { get; init; } =
        ImmutableDictionary<Source, Ref<Seat>>.Empty;
}

/// <summary>
/// Reads <see cref="Seating"/> and the <see cref="Seat"/>s.
/// Reading through a watch's reader reruns the watch when a player changes seats.
/// </summary>
public static class SeatingReader
{
    /// <summary>
    /// The seat a player is sitting in, or <see cref="Ref{T}.None"/> when they have none.
    /// A player whose seat was removed has none until it's back.
    /// </summary>
    public static Ref<Seat> SeatOf(this IAtomReader R, Source? source)
    {
        if (source is { } assigned)
        {
            var hasSeat = R.Get(Seating.Only).Seats.TryGetValue(assigned, out var seat);
            var notDeleted = R.Get(Seat.ById, seat) is { Deleted: false };
            return hasSeat && notDeleted ? seat : Ref<Seat>.None;
        }

        return Ref<Seat>.None;
    }

    /// <summary>
    /// The seat the local player is sitting in, or <see cref="Ref{T}.None"/>.
    /// </summary>
    public static Ref<Seat> LocalSeat(this IAtomReader R) => R.SeatOf(Source.Me);

    /// <summary>
    /// Whether a player is sitting in <paramref name="seat"/>.
    /// </summary>
    public static bool IsSeatTaken(this IAtomReader R, Ref<Seat> seat) =>
        R.Get(Seating.Only).Seats.Values.Contains(seat);

    /// <summary>
    /// The color of the seat a player sits in, or gray when they have none.
    /// </summary>
    public static Color SeatColor(this IAtomReader R, Source source) =>
        R.Get(Seat.ById, R.SeatOf(source))?.Color ?? new Color(0.8f, 0.8f, 0.8f);
}
