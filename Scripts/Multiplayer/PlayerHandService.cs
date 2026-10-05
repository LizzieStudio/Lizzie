using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A player hand is a container which tracks which cards are displayed on player's screens.
/// </summary>
public partial class PlayerHandService : Node
{
    private static PlayerHandService _instance;
    public static PlayerHandService Instance => _instance;

    public override void _EnterTree()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    /// <summary>
    /// The card moved into a seat's hand at the top of its order.
    /// Every card that shares a <paramref name="stamp"/> should have a unique <paramref name="suborder"/>.
    /// </summary>
    public ComponentState MovedToHand(
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
    /// Returns an empty list for observer seats or when the scene is not ready.
    /// </summary>
    public IReadOnlyList<VcToken> GetHand(int seatIndex)
    {
        if (seatIndex < 0)
            return System.Array.Empty<VcToken>();

        var gameObjects = ProjectService.Instance?.GameObjects;
        if (gameObjects == null)
            return System.Array.Empty<VcToken>();

        var container = RecordService.Instance.HandOf(seatIndex);
        if (container == SnowTag.Empty)
            return System.Array.Empty<VcToken>();

        return gameObjects.GetContainedComponents(container).OfType<VcToken>().ToList();
    }
}
