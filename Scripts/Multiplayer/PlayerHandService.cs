using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reads and fills the player hands.
/// </summary>
public static class PlayerHandService
{
    /// <summary>
    /// The card moved into a seat's hand at the top of its order.
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
            ContainerRef = RecordService.Instance.HandOf(seatIndex),
            ZOrder = new ZOrder(ZTarget.Top, suborder, stamp),
        };

    /// <summary>
    /// Returns the cards in a given seat's hand, ordered by their ZOrder.
    /// Returns an empty list for observer seats.
    /// </summary>
    public static IReadOnlyList<ComponentState> GetHand(IRecordReader R, int seatIndex)
    {
        var container = R.HandOf(seatIndex);
        if (container == SnowTag.Empty)
            return [];

        return R.Get<ComponentState>(s => s.ContainerRef == container)
            .OrderBy(s => s.ZOrder)
            .ToList();
    }
}
