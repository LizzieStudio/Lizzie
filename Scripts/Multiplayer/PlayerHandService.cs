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
    /// Gets the container id for a seat's hand.
    /// </summary>
    public SnowTag HandContainer(int seatIndex) =>
        PresenceSynchronizer.Instance?.HandRefForSeat(seatIndex) ?? SnowTag.Empty;

    /// <summary>
    /// Builds a transform effect that moves a card into a seat's hand at the top of its order.
    /// </summary>
    public ComponentEffect MoveEffect(VisualComponentBase card, int seatIndex, int suborder)
    {
        var e = ComponentEffect.Capture(card);
        e.State.Location = VisualComponentBase.ComponentLocation.Hand;
        e.State.ContainerRef = HandContainer(seatIndex);
        e.State.ZOrder = new ZOrder(ZTarget.Top, suborder, SnowportId.Empty);
        return e;
    }

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

        var container = HandContainer(seatIndex);
        if (container == SnowTag.Empty)
            return System.Array.Empty<VcToken>();

        return gameObjects.GetContainedComponents(container).OfType<VcToken>().ToList();
    }

    /// <summary>
    /// Returns the seat index the local player is sitting in (-2 if unclaimed).
    /// Works in both local-only and multiplayer modes.
    /// </summary>
    public static int LocalSeatIndex()
    {
        var psm = PresenceSynchronizer.Instance;
        if (psm == null)
            return 0; // safe default for solo mode

        int seat = psm.GetSeatBySource(Snowport.Clock.source);
        return seat == -2 ? 0 : seat; // fall back to seat 0
    }
}
