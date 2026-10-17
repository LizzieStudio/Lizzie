using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reads and fills the player hands.
/// </summary>
public static class PlayerHandService
{
    /// <summary>
    /// The card moved into a seat's hand at the top of its order, upright.
    /// Every card that shares a <paramref name="stamp"/> should have a unique <paramref name="suborder"/>.
    /// </summary>
    public static ComponentState MovedToHand(
        ComponentState card,
        int seatIndex,
        int suborder,
        SnowportId stamp
    ) =>
        card with
        {
            ContainerRef = AtomService.Instance.HandOf(seatIndex),
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
    /// <param name="handRef">The hand's container.</param>
    public static IEnumerable<ComponentState> Inserted(
        IReadOnlyList<ComponentState> hand,
        IReadOnlyList<ComponentState> cards,
        int index,
        SnowTag handRef,
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
                        ContainerRef = handRef,
                        ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                    }
            );
    }

    /// <summary>
    /// Returns the cards in a given seat's hand in a stage, ordered by their ZOrder.
    /// Returns an empty list for observer seats.
    /// </summary>
    public static IReadOnlyList<ComponentState> GetHand(IAtomReader R, SnowTag stage, int seatIndex)
    {
        var container = R.HandOf(seatIndex);
        if (container == SnowTag.Empty)
            return [];

        return R.Get(ComponentState.ByContainer, new StageKey(stage, container))
            .OrderBy(s => s.ZOrder)
            .ToList();
    }
}
