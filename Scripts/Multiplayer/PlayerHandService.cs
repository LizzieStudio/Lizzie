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

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
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
    public SnowportId HandContainer(int seatIndex) =>
        PlayerSeatManager.Instance?.HandRefForSeat(seatIndex) ?? SnowportId.Empty;

    /// <summary>
    /// Builds a transform effect that moves a card into a seat's hand at the top of its order.
    /// </summary>
    public TransformEffect MoveEffect(VisualComponentBase card, int seatIndex, int suborder) =>
        new()
        {
            Id = card.Reference,
            Location = VisualComponentBase.ComponentLocation.Hand,
            ContainerRef = HandContainer(seatIndex),
            Position = card.Position,
            Rotation = card.Rotation,
            ZTarget = ZTarget.Top,
            ZSuborder = suborder,
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

        var container = HandContainer(seatIndex);
        if (container == SnowportId.Empty)
            return System.Array.Empty<VcToken>();

        return gameObjects.GetContainedComponents(container).OfType<VcToken>().ToList();
    }

    /// <summary>
    /// Returns the seat index the local player is sitting in (-2 if unclaimed).
    /// Works in both local-only and multiplayer modes.
    /// </summary>
    public static int LocalSeatIndex()
    {
        var psm = PlayerSeatManager.Instance;
        if (psm == null)
            return 0; // safe default for solo mode

        int seat = psm.GetSeatBySource(Snowport.Clock.source);
        return seat == -2 ? 0 : seat; // fall back to seat 0
    }
}
