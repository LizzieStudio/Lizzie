using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Tracks which connection owns which player position.</summary>
public partial class PlayerSeatManager : Node
{
    private static PlayerSeatManager _instance;
    public static PlayerSeatManager Instance => _instance;

    /// <summary>Raised after the seat registry has been updated for an event.</summary>
    [Signal]
    public delegate void SeatsChangedEventHandler();

    /// <summary>The current state of a player, keyed by the player's own SnowTag.</summary>
    private sealed class PlayerRecord
    {
        public SnowTag Id;
        public int Seat;
        public SnowTag HandRef;
        public SnowTag CursorRef;
        public bool HasLeft;

        /// <summary>The id of the event that last wrote this record.</summary>
        public SnowportId LastUpdateId;
    }

    private readonly Dictionary<SnowTag, PlayerRecord> _players = new();

    private SnowTag _localPlayerId = SnowTag.Empty;

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

    public void Clear()
    {
        _players.Clear();
        _localPlayerId = SnowTag.Empty;
    }

    /// <summary>Returns true if the seat is not owned by anyone.</summary>
    public bool IsAvailable(int seatIndex) => seatIndex == -1 || SeatOwner(seatIndex) == null;

    /// <summary>
    /// Returns the effective seat occupied by the client with the given Snowport source,
    /// or -2 if it is unseated or was displaced from its chosen seat.
    /// </summary>
    public int GetSeatBySource(byte source)
    {
        var r = ActiveForSource(source);
        if (r == null)
            return -2;
        if (r.Seat < 0)
            return r.Seat;
        return SeatOwner(r.Seat)?.Id == r.Id ? r.Seat : -2;
    }

    /// <summary>The hand container id owned by the current occupant of a seat.</summary>
    public SnowTag HandRefForSeat(int seatIndex) => SeatOwner(seatIndex)?.HandRef ?? SnowTag.Empty;

    /// <summary>Claim a seat for the local player.</summary>
    public void ClaimSeat(int seatIndex)
    {
        if (_localPlayerId == SnowTag.Empty)
            _localPlayerId = Snowport.Clock.CreateTag();

        _players.TryGetValue(_localPlayerId, out var mine);

        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdatePlayerEffect
                {
                    Id = _localPlayerId,
                    Seat = seatIndex,
                    HandRef =
                        mine != null && mine.HandRef != SnowTag.Empty
                            ? mine.HandRef
                            : Snowport.Clock.CreateTag(),
                    CursorRef =
                        mine != null && mine.CursorRef != SnowTag.Empty
                            ? mine.CursorRef
                            : Snowport.Clock.CreateTag(),
                    HasLeft = false,
                }
            )
        );
    }

    /// <summary>
    /// Seats the local participant in solo play.
    /// No-op in multiplayer or if the local participant is already seated.
    /// </summary>
    public void EnsureLocalSeat(int seatIndex = 0)
    {
        if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return;
        if (GetSeatBySource(Snowport.Clock.source) != -2)
            return; // already seated
        ClaimSeat(seatIndex);
    }

    /// <summary>Announce that a peer has left (server-only, on disconnect).</summary>
    public void ReleaseSeat(int peerId)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        if (MultiplayerManager.Instance.Players.TryGetValue(peerId, out var pi) != true)
            return;

        var r = ActiveForSource(pi.Source);
        if (r == null)
            return;

        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdatePlayerEffect
                {
                    Id = r.Id,
                    Seat = r.Seat,
                    HandRef = r.HandRef,
                    CursorRef = r.CursorRef,
                    HasLeft = true,
                }
            )
        );
    }

    /// <summary>Captures the active players as upsert effects for a late joiner.</summary>
    public Effect[] GenerateCatchupEffects() =>
        _players
            .Values.Where(r => !r.HasLeft)
            .Select(r =>
                (Effect)
                    new UpdatePlayerEffect
                    {
                        Id = r.Id,
                        Seat = GetSeatBySource(r.Id.source),
                        HandRef = r.HandRef,
                        CursorRef = r.CursorRef,
                        HasLeft = false,
                    }
            )
            .ToArray();

    private void OnEventApplied(TableEvent e)
    {
        bool changed = false;

        foreach (var effect in e.Effects)
        {
            if (effect is not UpdatePlayerEffect up)
                continue;

            if (
                _players.TryGetValue(up.Id, out var current)
                && e.Id.CompareTo(current.LastUpdateId) < 0
            )
                continue;

            byte localSource = Snowport.Clock.source;
            int localSeatBefore = GetSeatBySource(localSource);

            _players[up.Id] = new PlayerRecord
            {
                Id = up.Id,
                Seat = up.Seat,
                HandRef = up.HandRef,
                CursorRef = up.CursorRef,
                HasLeft = up.HasLeft,
                LastUpdateId = e.Id,
            };
            changed = true;

            if (
                up.Id != _localPlayerId
                && localSeatBefore >= 0
                && GetSeatBySource(localSource) == -2
            )
            {
                GD.Print(
                    "[PlayerSeatManager] Displaced from seat by a newer claim – prompting again"
                );
                EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
            }
        }

        if (changed)
            EmitSignal(SignalName.SeatsChanged);
    }

    /// <summary>The most recent active claimant of a seat, or null.</summary>
    private PlayerRecord SeatOwner(int seat)
    {
        if (seat < 0)
            return null;

        PlayerRecord best = null;
        foreach (var r in _players.Values)
        {
            if (r.HasLeft || r.Seat != seat)
                continue;
            if (best == null || r.LastUpdateId.CompareTo(best.LastUpdateId) > 0)
                best = r;
        }
        return best;
    }

    private PlayerRecord ActiveForSource(byte source)
    {
        foreach (var r in _players.Values)
            if (!r.HasLeft && r.Id.source == source)
                return r;
        return null;
    }
}
