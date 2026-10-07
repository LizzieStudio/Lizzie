using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

/// <summary>
/// Tracks session state in multiplayer.
/// <list type="bullet">
/// <item>seat requests, which it writes to <see cref="Seating"/></item>
/// <item>each client's cursor position, which <see cref="CursorOverlay"/> draws</item>
/// </list>
/// </summary>
public partial class PresenceSynchronizer : Node
{
    private static PresenceSynchronizer _instance;
    public static PresenceSynchronizer Instance => _instance;

    private const double SendInterval = 1.0 / 30.0;

    private const float MoveEpsilon = 0.1f;

    private static Vector3 Miss => DragPlane.Miss;

    private DragPlane _dragPlane;

    private double _sendAccumulator;
    private Vector3 _lastSentPosition = Miss;

    // The local player's cursor, kept apart so it stays theirs when their source changes.
    private Vector3? _localPosition;

    // The other players' cursors, by source.
    private readonly Dictionary<byte, Vector3> _positions = new();

    public bool TryGetCursor(byte source, out Vector3 pos)
    {
        if (source == Snowport.Clock.source)
        {
            pos = _localPosition.GetValueOrDefault();
            return _localPosition.HasValue;
        }
        return _positions.TryGetValue(source, out pos);
    }

    /// <summary>
    /// The cursor positions of other players on the table.
    /// </summary>
    public IReadOnlyDictionary<byte, Vector3> RemoteCursors => _positions;

    public override void _EnterTree()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public void SetContext(DragPlane dragPlane)
    {
        _dragPlane = dragPlane;
    }

    public void ClearContext()
    {
        _dragPlane = null;
        _lastSentPosition = Miss;
        ClearCursors();
    }

    #region Seats

    private static RecordService R => RecordService.Instance;

    /// <summary>
    /// The cursor position of the seated player whose cursor container is <paramref name="cursorRef"/>.
    /// </summary>
    public bool TryGetCursor(SnowTag cursorRef, out Vector3 pos)
    {
        if (R.HolderOf(cursorRef) is byte source)
            return TryGetCursor(source, out pos);
        pos = default;
        return false;
    }

    /// <summary>
    /// Asks for a seat for the local player, or <see cref="SeatingReader.NoSeat"/> to observe.
    /// The host or a solo player takes it at once; a client asks the host.
    /// </summary>
    public void RequestSeat(int seat)
    {
        var mm = MultiplayerManager.Instance;
        if (mm?.IsMultiplayerActive == true && !mm.IsServer)
            RpcId(1, nameof(ServerRequestSeat), seat);
        else
            TryAssignSeat(Snowport.Clock.source, seat, 1);
    }

    /// <summary>
    /// In solo play, seats the local player alone, keeping their seat if they have one, or seat 0.
    /// </summary>
    public void SeatSoloPlayer()
    {
        if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return;

        var source = Snowport.Clock.source;
        var seats = R.Single<Seating>().Seats;
        var seated = seats.TryGetValue(source, out var current);
        if (seated && seats.Count == 1)
            return;
        WriteSeats(ImmutableDictionary<byte, int>.Empty.Add(source, seated ? current : 0));
    }

    /// <summary>On the server, free the seat held by a disconnected peer's source.</summary>
    public void ReleaseSeatForPeer(int peerId)
    {
        var mm = MultiplayerManager.Instance;
        if (mm?.IsServer != true)
            return;
        if (!mm.Players.TryGetValue(peerId, out var pi))
            return;

        var seats = R.Single<Seating>().Seats;
        if (seats.ContainsKey(pi.Source))
            WriteSeats(seats.Remove(pi.Source));
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ServerRequestSeat(int seat)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        var peer = Multiplayer.GetRemoteSenderId();
        if (MultiplayerManager.Instance.Players.TryGetValue(peer, out var pi) != true)
            return;

        TryAssignSeat(pi.Source, seat, peer);
    }

    /// <summary>
    /// The host's decision: a seat goes to whoever asks first, and anyone later asks again.
    /// </summary>
    private void TryAssignSeat(byte source, int seat, int requesterPeerId)
    {
        if (R.IsSeatTaken(seat) && R.SeatOf(source) != seat)
        {
            if (requesterPeerId == 1)
                OnSeatDenied();
            else
                RpcId(requesterPeerId, nameof(SeatDenied));
            return;
        }

        Seat(source, seat);
    }

    private static void Seat(byte source, int seat)
    {
        var seats = R.Single<Seating>().Seats;
        WriteSeats(
            seat == SeatingReader.NoSeat ? seats.Remove(source) : seats.SetItem(source, seat)
        );
    }

    private static void WriteSeats(ImmutableDictionary<byte, int> seats) =>
        R.WriteAdmin(R.Single<Seating>() with { Seats = seats });

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void SeatDenied() => OnSeatDenied();

    private void OnSeatDenied()
    {
        GD.Print("[PresenceSynchronizer] Seat unavailable - prompting again");
        EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
    }

    #endregion

    #region Cursor Streaming

    public override void _Process(double delta)
    {
        if (_dragPlane == null)
            return;

        var pos = _dragPlane.GetCursorProjection();
        if (pos != Miss)
            _localPosition = pos;

        if (MultiplayerManager.Instance != null)
            RemoveStaleCursors(MultiplayerManager.Instance);

        if (MultiplayerManager.Instance?.IsMultiplayerActive != true)
            return;

        _sendAccumulator += delta;
        if (_sendAccumulator < SendInterval)
            return;
        _sendAccumulator = 0;

        if (pos == Miss)
            return;
        if (_lastSentPosition != Miss && pos.DistanceTo(_lastSentPosition) < MoveEpsilon)
            return;
        _lastSentPosition = pos;

        if (MultiplayerManager.Instance.IsServer)
            Rpc(nameof(ClientReceiveCursor), (int)Snowport.Clock.source, pos);
        else
            RpcId(1, nameof(ServerReceiveCursor), (int)Snowport.Clock.source, pos);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered
    )]
    private void ServerReceiveCursor(int source, Vector3 pos)
    {
        var mm = MultiplayerManager.Instance;
        if (mm?.IsServer != true)
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        UpdateCursor((byte)source, pos);

        foreach (var player in mm.Players)
        {
            if (player.Key == senderId || player.Key == 1)
                continue;
            RpcId(player.Key, nameof(ClientReceiveCursor), source, pos);
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered
    )]
    private void ClientReceiveCursor(int source, Vector3 pos)
    {
        UpdateCursor((byte)source, pos);
    }

    private void UpdateCursor(byte source, Vector3 pos)
    {
        if (_dragPlane == null)
            return;
        if (source == Snowport.Clock.source)
            return;

        _positions[source] = pos;
    }

    private static bool IsSourceConnected(MultiplayerManager mm, byte source)
    {
        foreach (var p in mm.Players.Values)
        {
            if (p.Source == source)
                return true;
        }

        return false;
    }

    private void RemoveStaleCursors(MultiplayerManager mm)
    {
        if (_positions.Count == 0)
            return;

        List<byte> stale = null;
        foreach (var source in _positions.Keys)
        {
            if (!IsSourceConnected(mm, source))
                (stale ??= new List<byte>()).Add(source);
        }

        if (stale == null)
            return;

        foreach (var source in stale)
            _positions.Remove(source);
    }

    private void ClearCursors()
    {
        _localPosition = null;
        _positions.Clear();
    }

    #endregion
}
