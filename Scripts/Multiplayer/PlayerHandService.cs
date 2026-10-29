using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie;

/// <summary>
/// Reads and fills the player hands.
/// </summary>
public static class PlayerHandService
{
    /// <summary>
    /// The card moved into a seat's hand face-up at <paramref name="rank"/>.
    /// </summary>
    public static ComponentPosition MovedToHand(
        ComponentPosition card,
        Ref<Seat> seat,
        SnowRank rank
    ) =>
        card with
        {
            Container = new InHand(seat),
            Rotation = card.Rotation with { Y = 0, Z = 0 },
            Rank = rank,
        };

    /// <summary>
    /// The atoms that put <paramref name="cards"/> into a hand at <paramref name="index"/>, in their order.
    /// </summary>
    /// <param name="hand">The hand's cards, in order, which can include members of <paramref name="cards"/>.</param>
    /// <param name="seat">The seat whose hand it is.</param>
    public static IEnumerable<ComponentPosition> Inserted(
        IReadOnlyList<ComponentPosition> hand,
        IReadOnlyList<ComponentPosition> cards,
        int index,
        Ref<Seat> seat
    )
    {
        var moving = cards.Select(c => c.Id).ToHashSet();
        var rest = hand.Where(c => !moving.Contains(c.Id)).ToList();
        var ranks = SnowRank.At(rest, Math.Clamp(index, 0, rest.Count), cards.Count);
        return cards.Select((c, i) => c with { Container = new InHand(seat), Rank = ranks[i] });
    }

    /// <summary>
    /// Returns the cards in a seat's hand in a stage, ordered by their rank.
    /// </summary>
    public static IReadOnlyList<ComponentPosition> GetHand(
        IAtomReader R,
        Ref<Stage> stage,
        Ref<Seat> seat
    ) => R.Get(ComponentPosition.ByContainer, new(stage, new InHand(seat)));
}
