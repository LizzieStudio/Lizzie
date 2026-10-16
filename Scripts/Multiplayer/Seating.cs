using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>
/// <para>Which seat each player is sitting in, by their Snowport source.</para>
/// <para>
/// Only the host or a solo player writes it, as admin events, so no one can undo a seat change.
/// <see cref="PresenceSynchronizer"/> takes seat requests and decides between players asking for the same seat.
/// It's not saved: a loaded project starts with no one seated, and a solo player takes seat 0.
/// A player without a seat watches, whether they chose to observe or haven't chosen yet.
/// </para>
/// </summary>
[NotSaved]
[JsonName("Seating")]
public sealed record Seating : Atom
{
    public static readonly Singleton<Seating> Only = new();

    /// <summary>The seat of each seated player. Players without a seat are missing.</summary>
    public ImmutableDictionary<byte, int> Seats { get; init; } =
        ImmutableDictionary<byte, int>.Empty;
}

/// <summary>
/// Reads <see cref="Seating"/> and the seats in <see cref="ProjectGameSettings"/>.
/// Reading through a watch's reader reruns the watch when a player changes seats.
/// </summary>
public static class SeatingReader
{
    /// <summary>The seat of a player who watches: an observer, or a player who hasn't chosen a seat.</summary>
    public const int NoSeat = -1;

    /// <summary>The seat a player is sitting in, or <see cref="NoSeat"/>.</summary>
    public static int SeatOf(this IAtomReader R, byte source) =>
        R.Get(Seating.Only).Seats.TryGetValue(source, out var seat) ? seat : NoSeat;

    /// <summary>The seat the local player is sitting in, or <see cref="NoSeat"/>.</summary>
    public static int LocalSeat(this IAtomReader R) => R.SeatOf(Snowport.Clock.source);

    /// <summary>Whether a player is sitting in <paramref name="seat"/>. <see cref="NoSeat"/> is never taken.</summary>
    public static bool IsSeatTaken(this IAtomReader R, int seat) =>
        R.Get(Seating.Only).Seats.Values.Contains(seat);

    /// <summary>A seat's settings, or null for <see cref="NoSeat"/> or a seat that's been removed.</summary>
    public static ProjectPlayerSettings SeatSettings(this IAtomReader R, int seat)
    {
        var players = R.Get(ProjectGameSettings.Only).Players;
        return seat >= 0 && seat < players.Length ? players[seat] : null;
    }

    /// <summary>The container for a seat's hand, or <see cref="SnowTag.Empty"/>.</summary>
    public static SnowTag HandOf(this IAtomReader R, int seat) =>
        R.SeatSettings(seat)?.HandRef ?? SnowTag.Empty;

    /// <summary>
    /// The container holding what the local player drags, or <see cref="SnowTag.Empty"/> when
    /// they have no seat, so they can't drag.
    /// </summary>
    public static SnowTag LocalCursor(this IAtomReader R) =>
        R.SeatSettings(R.LocalSeat())?.CursorRef ?? SnowTag.Empty;

    /// <summary>
    /// The container holding what the local player drags over their hand,
    /// or <see cref="SnowTag.Empty"/> when they have no seat.
    /// </summary>
    public static SnowTag LocalHandHold(this IAtomReader R) =>
        R.SeatSettings(R.LocalSeat())?.HandHoldRef ?? SnowTag.Empty;

    /// <summary>
    /// The player whose seat drags with <paramref name="cursorRef"/>, if there is one.
    /// </summary>
    public static byte? HolderOf(this IAtomReader R, SnowTag cursorRef)
    {
        var players = R.Get(ProjectGameSettings.Only).Players;
        foreach (var (source, seat) in R.Get(Seating.Only).Seats)
            if (
                seat < players.Length
                && (players[seat].CursorRef == cursorRef || players[seat].HandHoldRef == cursorRef)
            )
                return source;
        return null;
    }

    /// <summary>The color of the seat a player sits in, or gray when they have none.</summary>
    public static Color SeatColor(this IAtomReader R, byte source) =>
        R.SeatSettings(R.SeatOf(source))?.Color ?? new Color(0.8f, 0.8f, 0.8f);
}
