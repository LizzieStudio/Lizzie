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

    /// <summary>A map to the hand container id assigned when that seat was joined.</summary>
    private readonly Dictionary<int, SnowportId> _handRefs = new();

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += OnEventApplied;
    }

    public override void _ExitTree()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied -= OnEventApplied;

        if (_instance == this)
            _instance = null;
    }

    public void Clear() => _handRefs.Clear();

    private void OnEventApplied(TableEvent e)
    {
        switch (e.Action)
        {
            case PlayerJoinAction j when j.Seat >= 0:
                _handRefs[j.Seat] = j.HandRef;
                break;
            case PlayerLeaveAction l:
                // TODO the cards are left orphaned
                _handRefs.Remove(l.Seat);
                break;
        }
    }

    /// <summary>
    /// Gets the container id for a seat's hand.
    /// </summary>
    public SnowportId HandContainer(int seatIndex)
    {
        if (_handRefs.TryGetValue(seatIndex, out var id))
            return id;

        if (MultiplayerManager.Instance?.IsMultiplayerActive != true)
        {
            id = Snowport.Clock.Create();
            _handRefs[seatIndex] = id;
            return id;
        }

        return SnowportId.Empty;
    }

    /// <summary>
    /// Builds a transform effect that moves a card into a seat's hand at the top of its order.
    /// </summary>
    public TransformEffect MoveEffect(VisualComponentBase card, int seatIndex, int suborder) =>
        new()
        {
            ComponentRef = card.Reference,
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
        var mm = MultiplayerManager.Instance;
        var psm = PlayerSeatManager.Instance;
        if (mm == null || psm == null)
            return 0; // safe default for solo mode

        int localPeerId = mm.IsMultiplayerActive ? mm.LocalPlayerId : 1;
        int seat = psm.GetSeat(localPeerId);
        return seat == -2 ? 0 : seat; // fall back to seat 0
    }
}
