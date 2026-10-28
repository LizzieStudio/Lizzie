using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiGodot;

namespace Lizzie;

/// <summary>
/// A place at the table for one player, with its own hand.
/// Players sit in seats through <see cref="Seating"/>.
/// </summary>
[JsonName("Seat")]
public sealed record Seat : Atom, IRanked
{
    public static readonly Identity<Seat, Ref<Seat>> ById = new(s => s.Id);

    /// <summary>
    /// The seats that aren't deleted, in order.
    /// </summary>
    public static readonly All<Seat> Live = new(s => !s.Deleted, IRanked.ByRank);

    /// <summary>
    /// This atom's identity, created with <see cref="Ref{T}.Create"/>.
    /// </summary>
    public required Ref<Seat> Id { get; init; }

    /// <summary>
    /// Its place among the seats.
    /// </summary>
    public SnowRank Rank { get; init; }

    /// <summary>
    /// Its name, or null to go by its place, like "Seat 2".
    /// </summary>
    public string Name { get; init; }

    public Color Color { get; init; }

    /// <summary>
    /// Whether its player sees and moves everything, regardless of what the zones say.
    /// </summary>
    public bool IsAdmin { get; init; }

    public bool Deleted { get; init; }

    /// <summary>
    /// What to call it at <paramref name="place"/> among the seats, counting from 0.
    /// </summary>
    public string Label(int place) => string.IsNullOrEmpty(Name) ? $"Seat {place + 1}" : Name;

    /// <summary>
    /// A new unnamed seat at <paramref name="rank"/>, in a color none of the <paramref name="others"/> have.
    /// </summary>
    public static Seat New(SnowRank rank, IEnumerable<Seat> others) =>
        new()
        {
            Id = Ref<Seat>.Create(),
            Rank = rank,
            Color = NextColor(others),
        };

    /// <summary>
    /// The first color in the seat palette that none of <paramref name="seats"/> have.
    /// </summary>
    public static Color NextColor(IEnumerable<Seat> seats)
    {
        // This could be improved, but it gives us infinite (kind of) distinct colors.
        var taken = seats.Select(s => s.Color.ToVector3());
        var candidates = Enumerable
            .Range(0, 1000)
            .Select(n =>
            {
                // The golden angle "picks" distinct colors on the color wheel.
                var goldenAngle = (3.0 - Math.Sqrt(5.0)) / 2.0;
                var hue = n * goldenAngle;
                // Every time we go around the hues, switch lightness.
                var lightness = 0.5 + Math.Sin(Math.PI * goldenAngle * Math.Floor(hue)) / 5.0;
                return Color.FromOkHsl((float)hue, 0.9f, (float)lightness);
            });
        return candidates.FirstOrDefault(color =>
        {
            var vect = color.ToVector3();
            // check the distance to make sure we're not using too similar of a color.
            return taken.All(t => (t - vect).Length() > 0.05);
        });
    }

    /// <summary>
    /// The seats a new project starts with.
    /// </summary>
    public static List<Seat> Defaults()
    {
        var seats = new List<Seat>();
        SnowRank rank = default;
        for (int i = 0; i < 4; i++)
            seats.Add(New(rank = rank.After(SnowportId.Create()), seats));
        return seats;
    }
}

/// <summary>
/// A seat in the project settings being dragged to a new place, as Godot drag-and-drop data.
/// </summary>
public sealed partial class SeatDrag : RefCounted
{
    public Ref<Seat> Seat { get; init; }
}
