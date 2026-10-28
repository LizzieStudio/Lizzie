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
    /// The card moved into a seat's hand at the top of its order, upright.
    /// Every card that shares a <paramref name="stamp"/> should have a unique <paramref name="suborder"/>.
    /// </summary>
    public static ComponentPosition MovedToHand(
        ComponentPosition card,
        Ref<Seat> seat,
        int suborder,
        SnowportId stamp
    ) =>
        card with
        {
            Container = new InHand(seat),
            Rotation = card.Rotation with { Y = 0 },
            ZOrder = new ZOrder(ZTarget.Top, suborder, stamp),
        };

    /// <summary>
    /// The atoms that put <paramref name="cards"/> into a hand at <paramref name="index"/>, in their order.
    ///
    /// <para>
    /// The cards to the right are rewritten with a new ZOrder to make room.
    /// Every card that shares a <paramref name="stamp"/> should be in the same call.
    /// </para>
    /// </summary>
    /// <param name="hand">The hand's cards, in order, which can include members of <paramref name="cards"/>.</param>
    /// <param name="seat">The seat whose hand it is.</param>
    public static IEnumerable<ComponentPosition> Inserted(
        IReadOnlyList<ComponentPosition> hand,
        IReadOnlyList<ComponentPosition> cards,
        int index,
        Ref<Seat> seat,
        SnowportId stamp
    )
    {
        var moving = cards.Select(c => c.Id).ToHashSet();
        var rest = hand.Where(c => !moving.Contains(c.Id)).ToList();
        index = Math.Clamp(index, 0, rest.Count);
        return cards
            .Concat(rest.Skip(index))
            .Select(
                (c, i) =>
                    c with
                    {
                        Container = new InHand(seat),
                        ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                    }
            );
    }

    /// <summary>
    /// Returns the cards in a seat's hand in a stage, ordered by their ZOrder.
    /// Returns an empty list for <see cref="Ref{T}.Empty"/>, the seat of a player without one.
    /// </summary>
    public static IReadOnlyList<ComponentPosition> GetHand(
        IAtomReader R,
        Ref<Stage> stage,
        Ref<Seat> seat
    ) =>
        seat.IsEmpty
            ? []
            : R.Get(ComponentPosition.ByContainer, new(stage, new InHand(seat)))
                .OrderBy(s => s.ZOrder)
                .ToList();
}
