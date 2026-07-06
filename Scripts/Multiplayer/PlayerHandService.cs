using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Singleton that owns per-seat hand collections.
/// SeatIndex 0..N correspond to GameSettings.Players entries.
/// Observers (SeatIndex -1) and unclaimed seats (-2) have no hand.
/// </summary>
public partial class PlayerHandService : Node
{
    private static PlayerHandService _instance;
    public static PlayerHandService Instance => _instance;

    // seatIndex -> ordered list of VcToken cards in that hand
    private readonly Dictionary<int, List<VcToken>> _hands = new();

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

        EventBus.Instance.Subscribe<AddToHandEvent>(OnAddToHand);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the cards in a given seat's hand (read-only view).
    /// Returns an empty list for unknown or observer seats.
    /// </summary>
    public IReadOnlyList<VcToken> GetHand(int seatIndex)
    {
        if (seatIndex < 0)
            return System.Array.Empty<VcToken>();
        return _hands.TryGetValue(seatIndex, out var list) ? list : System.Array.Empty<VcToken>();
    }

    /// <summary>
    /// All seats that currently have at least one card.
    /// </summary>
    public IEnumerable<int> ActiveSeats => _hands.Keys.Where(k => k >= 0 && _hands[k].Count > 0);

    /// <summary>
    /// Returns the seat index the local player is sitting in (-2 if unclaimed).
    /// Works in both local-only and multiplayer modes.
    /// </summary>
    public static int LocalSeatIndex()
    {
        var mm = MultiplayerManager.Instance;
        var psm = PlayerSeatManager.Instance;
        if (mm == null || psm == null)
            return 0; // safe default for local-only mode

        int localPeerId = mm.IsMultiplayerActive ? mm.LocalPlayerId : 1;
        int seat = psm.GetSeat(localPeerId);
        return seat == -2 ? 0 : seat; // fall back to seat 0 when unclaimed
    }

    /// <summary>
    /// Directly adds cards to a seat's hand and fires HandChangedEvent.
    /// </summary>
    public void AddCards(int seatIndex, IEnumerable<VcToken> cards)
    {
        if (seatIndex < 0)
            return;

        if (!_hands.ContainsKey(seatIndex))
            _hands[seatIndex] = new List<VcToken>();

        foreach (var card in cards)
        {
            card.Location = VisualComponentBase.ComponentLocation.Hand;
            _hands[seatIndex].Add(card);
        }

        EventBus.Instance.Publish(new HandChangedEvent { SeatIndex = seatIndex });
    }

    /// <summary>
    /// Removes a card from whichever seat holds it and fires HandChangedEvent.
    /// </summary>
    public void RemoveCard(VcToken card)
    {
        foreach (var kv in _hands)
        {
            if (kv.Value.Remove(card))
            {
                card.Location = VisualComponentBase.ComponentLocation.Board;
                EventBus.Instance.Publish(new HandChangedEvent { SeatIndex = kv.Key });
                return;
            }
        }
    }

    /// <summary>
    /// Returns the seat index that currently holds the given card, or -2 if none.
    /// </summary>
    public int FindSeat(VcToken card)
    {
        foreach (var kv in _hands)
        {
            if (kv.Value.Contains(card))
                return kv.Key;
        }
        return -2;
    }

    /// <summary>
    /// Clears all hands and fires HandChangedEvent for each affected seat.
    /// </summary>
    public void ClearAll()
    {
        var seats = _hands.Keys.ToList();
        _hands.Clear();
        foreach (var seat in seats)
            EventBus.Instance.Publish(new HandChangedEvent { SeatIndex = seat });
    }

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    private void OnAddToHand(AddToHandEvent evt)
    {
        int seat = evt.SeatIndex == -2 ? LocalSeatIndex() : evt.SeatIndex;
        AddCards(seat, evt.Cards);
    }
}
