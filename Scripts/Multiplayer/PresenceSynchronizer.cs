using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

/// <summary>
/// Tracks session state in multiplayer:
/// * seat requests, which it writes to <see cref="Seating"/>
/// * each client's cursor position
/// </summary>
public partial class PresenceSynchronizer : Node
{
    private static PresenceSynchronizer _instance;
    public static PresenceSynchronizer Instance => _instance;

    private const string CursorTexturePath = "res://Textures/cursor.png";

    private const double SendInterval = 1.0 / 30.0;

    private const float MoveEpsilon = 0.1f;

    private const float CursorLift = 0.2f;

    private static Vector3 Miss => DragPlane.Miss;

    private DragPlane _dragPlane;
    private Node3D _cursorParent;

    private double _sendAccumulator;
    private Vector3 _lastSentPosition = Miss;

    private Texture2D _cursorTexture;

    private readonly Dictionary<byte, Sprite3D> _cursors = new();

    private readonly Dictionary<byte, Vector3> _positions = new();

    public bool TryGetCursor(byte source, out Vector3 pos) =>
        _positions.TryGetValue(source, out pos);

    public override void _EnterTree()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public void SetContext(DragPlane dragPlane, Node3D cursorParent)
    {
        _dragPlane = dragPlane;
        _cursorParent = cursorParent;
    }

    public void ClearContext()
    {
        _dragPlane = null;
        _cursorParent = null;
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

    /// <summary>
    /// After leaving multiplayer, drops everyone else's seat.
    /// The local player keeps theirs, under their solo source.
    /// </summary>
    public void KeepOnlyLocalSeat(byte previousSource)
    {
        var seats = R.Single<Seating>().Seats;
        WriteSeats(
            seats.TryGetValue(previousSource, out var seat)
                ? ImmutableDictionary<byte, int>.Empty.Add(Snowport.Clock.source, seat)
                : ImmutableDictionary<byte, int>.Empty
        );
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

        var mm = MultiplayerManager.Instance;

        var pos = _dragPlane.GetCursorProjection();
        if (pos != Miss)
            _positions[Snowport.Clock.source] = pos;

        if (mm?.IsMultiplayerActive != true)
            return;

        RemoveStaleCursors(mm);

        _sendAccumulator += delta;
        if (_sendAccumulator < SendInterval)
            return;
        _sendAccumulator = 0;

        if (pos == Miss)
            return;
        if (_lastSentPosition != Miss && pos.DistanceTo(_lastSentPosition) < MoveEpsilon)
            return;
        _lastSentPosition = pos;

        if (mm.IsServer)
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
        if (_cursorParent == null)
            return;
        if (source == Snowport.Clock.source)
            return;

        if (!_cursors.TryGetValue(source, out var sprite))
        {
            sprite = CreateCursorSprite();
            _cursorParent.AddChild(sprite);
            _cursors[source] = sprite;
        }

        sprite.Modulate = R.SeatColor(source);
        sprite.Position = pos + Vector3.Up * CursorLift;

        _positions[source] = pos;
    }

    private Sprite3D CreateCursorSprite()
    {
        _cursorTexture ??= GD.Load<Texture2D>(CursorTexturePath);

        var sprite = new Sprite3D
        {
            Texture = _cursorTexture,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            NoDepthTest = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            PixelSize = 0.0003f,
        };

        var texSize = _cursorTexture.GetSize();
        sprite.Offset = new Vector2(texSize.X / 2f, -texSize.Y / 2f);

        return sprite;
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
        if (_cursors.Count == 0)
            return;

        List<byte> stale = null;
        foreach (var source in _cursors.Keys)
        {
            if (!IsSourceConnected(mm, source))
                (stale ??= new List<byte>()).Add(source);
        }

        if (stale == null)
            return;

        foreach (var source in stale)
        {
            if (_cursors.TryGetValue(source, out var sprite))
                sprite.QueueFree();
            _cursors.Remove(source);
            _positions.Remove(source);
        }
    }

    private void ClearCursors()
    {
        foreach (var sprite in _cursors.Values)
            sprite.QueueFree();
        _cursors.Clear();
        _positions.Clear();
    }

    #endregion
}
