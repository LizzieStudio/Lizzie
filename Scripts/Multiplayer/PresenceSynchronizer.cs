using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

/// <summary>
/// Tracks the players at the table.
/// <list type="bullet">
/// <item>seat requests, which it writes to <see cref="Seating"/></item>
/// <item>each player's cursor position, which <see cref="CursorOverlay"/> draws</item>
/// </list>
/// </summary>
public partial class PresenceSynchronizer : Node
{
    private static PresenceSynchronizer _instance;
    public static PresenceSynchronizer Instance => _instance;

    private const double SendInterval = 1.0 / 30.0;

    private DragPlane _dragPlane;

    private double _sendAccumulator;

    // Every player's cursor, by source. The local player's is the one this machine updates.
    private readonly Dictionary<byte, Vector3> _positions = new();

    /// <summary>
    /// Every player's cursor position on the table, by source.
    /// </summary>
    public IReadOnlyDictionary<byte, Vector3> Cursors => _positions;

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
        ClearCursors();
    }

    #region Seats

    private static AtomService R => AtomService.Instance;

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
        var seats = R.Get(Seating.Only).Seats;
        var seated = seats.TryGetValue(source, out var current);
        if (seated && seats.Count == 1)
            return;
        WriteSeats(ImmutableDictionary<byte, int>.Empty.Add(source, seated ? current : 0));
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
        var seats = R.Get(Seating.Only).Seats;
        WriteSeats(
            seat == SeatingReader.NoSeat ? seats.Remove(source) : seats.SetItem(source, seat)
        );
    }

    private static void WriteSeats(ImmutableDictionary<byte, int> seats) =>
        R.WriteAdmin(R.Get(Seating.Only) with { Seats = seats });

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
        if (_dragPlane?.GetCursorProjection() is not { } pos)
            return;
        _positions[Snowport.Clock.source] = pos;

        _sendAccumulator += delta;
        if (_sendAccumulator < SendInterval)
            return;
        _sendAccumulator = 0;

        if (
            Multiplayer.MultiplayerPeer?.GetConnectionStatus()
            == MultiplayerPeer.ConnectionStatus.Connected
        )
            Rpc(nameof(ReceiveCursor), pos);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered
    )]
    private void ReceiveCursor(Vector3 pos)
    {
        if (
            MultiplayerManager.Instance.Players.TryGetValue(
                Multiplayer.GetRemoteSenderId(),
                out var player
            )
            && player.Source != Snowport.AdminSource
        )
            _positions[player.Source] = pos;
    }

    /// <summary>
    /// The cursor position of the seated player whose cursor container is <paramref name="cursorRef"/>.
    /// </summary>
    public Vector3? CursorOf(SnowTag cursorRef) =>
        R.HolderOf(cursorRef) is byte source && _positions.TryGetValue(source, out var pos)
            ? pos
            : null;

    /// <summary>
    /// Forgets the cursor of <paramref name="source"/>, a player who left.
    /// </summary>
    public void RemoveCursor(byte source) => _positions.Remove(source);

    /// <summary>
    /// Forgets every cursor.
    /// </summary>
    public void ClearCursors() => _positions.Clear();

    #endregion
}
