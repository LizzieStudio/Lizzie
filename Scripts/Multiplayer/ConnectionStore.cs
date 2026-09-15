using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks which connected client occupies each seat.
/// </summary>
public partial class ConnectionStore : ReplicatedStore<Connection>
{
    private static ConnectionStore _instance;
    public static ConnectionStore Instance => _instance;

    /// <summary>Raised after the seat registry has been updated for an event.</summary>
    [Signal]
    public delegate void SeatsChangedEventHandler();

    private readonly Dictionary<SnowTag, Connection> _connections = new();

    private SnowTag _localPlayerId = SnowTag.Empty;

    protected override IDictionary<SnowTag, Connection> Store => _connections;

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += HandleApplied;
    }

    public override void _ExitTree()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied -= HandleApplied;

        if (_instance == this)
            _instance = null;
    }

    public void Clear()
    {
        _connections.Clear();
        _localPlayerId = SnowTag.Empty;
    }

    protected override void NotifyChanged(IReadOnlyList<SnowTag> ids) =>
        EmitSignal(SignalName.SeatsChanged);

    /// <summary>Merges connection records, then re-prompts if the local player was displaced.</summary>
    private void HandleApplied(TableEvent e)
    {
        byte localSource = Snowport.Clock.source;
        int before = GetSeatBySource(localSource);

        OnEventApplied(e);

        if (before >= 0 && GetSeatBySource(localSource) == -2)
        {
            GD.Print("[ConnectionStore] Displaced from seat by a newer claim – prompting again");
            EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
        }
    }

    /// <summary>Returns true if the seat is not owned by anyone.</summary>
    public bool IsAvailable(int seatIndex) => seatIndex == -1 || SeatOwner(seatIndex) == null;

    /// <summary>
    /// Returns the seat occupied by the client with the given Snowport source,
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

    /// <summary>The hand container id for a seat, read from the durable seat settings.</summary>
    public SnowTag HandRefForSeat(int seatIndex)
    {
        var players = ProjectService.Instance?.CurrentProject?.GameSettings?.Players;
        if (players == null || seatIndex < 0 || seatIndex >= players.Value.Length)
            return SnowTag.Empty;
        return players.Value[seatIndex].HandRef;
    }

    /// <summary>Claim a seat for the local player.</summary>
    public void ClaimSeat(int seatIndex)
    {
        if (_localPlayerId == SnowTag.Empty)
            _localPlayerId = Snowport.Clock.CreateTag();

        _connections.TryGetValue(_localPlayerId, out var mine);

        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<Connection>
                {
                    Id = _localPlayerId,
                    Payload = new Connection
                    {
                        Id = _localPlayerId,
                        Seat = seatIndex,
                        CursorRef =
                            mine != null && mine.CursorRef != SnowTag.Empty
                                ? mine.CursorRef
                                : Snowport.Clock.CreateTag(),
                        Deleted = false,
                    },
                }
            )
        );
    }

    /// <summary>
    /// Seats the local participant in solo play.
    /// </summary>
    public void EnsureLocalSeat(int seatIndex = 0)
    {
        if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return;
        if (GetSeatBySource(Snowport.Clock.source) != -2)
            return; // already seated
        ClaimSeat(seatIndex);
    }

    /// <summary>Announce that a peer has disconnected.</summary>
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
                new UpdateReplicatedEffect<Connection>
                {
                    Id = r.Id,
                    Payload = new Connection
                    {
                        Id = r.Id,
                        Seat = r.Seat,
                        CursorRef = r.CursorRef,
                        Deleted = true,
                    },
                }
            )
        );
    }

    /// <summary>The most recent active claimant of a seat, or null.</summary>
    private Connection SeatOwner(int seat)
    {
        if (seat < 0)
            return null;

        Connection best = null;
        foreach (var r in _connections.Values)
        {
            if (r.Deleted || r.Seat != seat)
                continue;
            if (best == null || r.LastUpdateId.CompareTo(best.LastUpdateId) > 0)
                best = r;
        }
        return best;
    }

    private Connection ActiveForSource(byte source)
    {
        foreach (var r in _connections.Values)
            if (!r.Deleted && r.Id.source == source)
                return r;
        return null;
    }
}
