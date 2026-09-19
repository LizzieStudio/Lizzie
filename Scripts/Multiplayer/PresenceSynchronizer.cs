using System.Collections.Generic;
using Godot;

/// <summary>
/// Tracks session state in multiplayer:
/// * which client occupies which seat
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

    /// <summary>Seat value meaning the source is unseated or has no claim.</summary>
    private const int Unseated = -2;

    private static Vector3 Miss => DragPlane.Miss;

    private static readonly Color FallbackColor = new(0.8f, 0.8f, 0.8f);

    private DragPlane _dragPlane;
    private Node3D _cursorParent;

    private double _sendAccumulator;
    private Vector3 _lastSentPosition = Miss;

    private Texture2D _cursorTexture;

    private readonly Dictionary<byte, Sprite3D> _cursors = new();

    private readonly Dictionary<byte, Vector3> _positions = new();

    /// <summary>Live seat occupancy: Snowport source to seat number map.</summary>
    private readonly Dictionary<byte, int> _seats = new();

    /// <summary>Raised after the seat registry changes.</summary>
    [Signal]
    public delegate void SeatsChangedEventHandler();

    public bool TryGetCursor(byte source, out Vector3 pos) =>
        _positions.TryGetValue(source, out pos);

    /// <summary>
    /// The cursor container of the seat the local player currently occupies.
    /// </summary>
    public SnowTag LocalCursorRef => CursorRefForSeat(GetSeatBySource(Snowport.Clock.source));

    public override void _Ready()
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

    #region Seat Registry

    /// <summary>Drops all live seat occupancy.</summary>
    public void Clear()
    {
        _seats.Clear();
        EmitSignal(SignalName.SeatsChanged);
    }

    /// <summary>The seat occupied by the given source, -1 for observer, or -2 if unseated.</summary>
    public int GetSeatBySource(byte source) =>
        _seats.TryGetValue(source, out var seat) ? seat : Unseated;

    /// <summary>True if the seat is an observer slot or is not currently occupied.</summary>
    public bool IsAvailable(int seat)
    {
        if (seat < 0)
            return true;
        foreach (var s in _seats.Values)
            if (s == seat)
                return false;
        return true;
    }

    /// <summary>The durable hand container for a seat, read from the project settings.</summary>
    public SnowTag HandRefForSeat(int seat)
    {
        var players = ProjectService.Instance?.CurrentProject?.GameSettings?.Players;
        if (players == null || seat < 0 || seat >= players.Value.Length)
            return SnowTag.Empty;
        return players.Value[seat].HandRef;
    }

    /// <summary>The durable cursor container for a seat, read from the project settings.</summary>
    public SnowTag CursorRefForSeat(int seat)
    {
        var players = ProjectService.Instance?.CurrentProject?.GameSettings?.Players;
        if (players == null || seat < 0 || seat >= players.Value.Length)
            return SnowTag.Empty;
        return players.Value[seat].CursorRef;
    }

    /// <summary>
    /// Requests a seat for the local player.
    /// In solo, the seat is taken immediately.
    /// In multiplayer, the server handles the result.
    /// </summary>
    public void RequestSeat(int seat)
    {
        var mm = MultiplayerManager.Instance;
        if (mm?.IsMultiplayerActive != true)
        {
            SetSeat(Snowport.Clock.source, seat);
            return;
        }

        if (mm.IsServer)
            TryAssignSeat(Snowport.Clock.source, seat, 1);
        else
            RpcId(1, nameof(ServerRequestSeat), seat);
    }

    /// <summary>
    /// Seats the local participant in solo play.
    /// </summary>
    public void EnsureLocalSeat(int seat = 0)
    {
        if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return;
        if (GetSeatBySource(Snowport.Clock.source) != Unseated)
            return;
        SetSeat(Snowport.Clock.source, seat);
    }

    /// <summary>On the server, free the seat held by a disconnected peer's source.</summary>
    public void ReleaseSeatForPeer(int peerId)
    {
        var mm = MultiplayerManager.Instance;
        if (mm?.IsServer != true)
            return;
        if (!mm.Players.TryGetValue(peerId, out var pi))
            return;
        if (!_seats.ContainsKey(pi.Source))
            return;

        Rpc(nameof(ClearSeat), (int)pi.Source);
    }

    /// <summary>On the server, stream the current seat map to a newly-joined peer.</summary>
    public void SendSeatSnapshotTo(int peerId)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;
        foreach (var kv in _seats)
            RpcId(peerId, nameof(SetSeat), (int)kv.Key, kv.Value);
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

    /// <summary>Server-side seat arbitration.</summary>
    private void TryAssignSeat(byte source, int seat, int requesterPeerId)
    {
        // A concrete seat must be free, unless the requester already holds it.
        if (seat >= 0 && !IsAvailable(seat) && GetSeatBySource(source) != seat)
        {
            if (requesterPeerId == 1)
                OnSeatDenied();
            else
                RpcId(requesterPeerId, nameof(SeatDenied));
            return;
        }

        Rpc(nameof(SetSeat), (int)source, seat);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void SetSeat(int source, int seat)
    {
        _seats[(byte)source] = seat;
        EmitSignal(SignalName.SeatsChanged);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClearSeat(int source)
    {
        if (_seats.Remove((byte)source))
            EmitSignal(SignalName.SeatsChanged);
    }

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

        sprite.Modulate = GetSeatColor(source);
        sprite.Position = pos + Vector3.Up * CursorLift;

        _positions[source] = pos;
    }

    /// <summary>
    /// Resolves the live cursor position for a seat's cursor container.
    /// </summary>
    public bool TryGetCursorByContainer(SnowTag containerRef, out Vector3 pos)
    {
        pos = default;

        int seat = SeatForCursorRef(containerRef);
        if (seat < 0)
            return false;

        foreach (var kv in _seats)
            if (kv.Value == seat)
                return _positions.TryGetValue(kv.Key, out pos);

        return false;
    }

    private static int SeatForCursorRef(SnowTag containerRef)
    {
        if (containerRef == SnowTag.Empty)
            return Unseated;
        var players = ProjectService.Instance?.CurrentProject?.GameSettings?.Players;
        if (players == null)
            return Unseated;
        for (int i = 0; i < players.Value.Length; i++)
            if (players.Value[i].CursorRef == containerRef)
                return i;
        return Unseated;
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

    private Color GetSeatColor(byte source)
    {
        var seat = GetSeatBySource(source);
        var settings = ProjectService.Instance?.CurrentProject?.GameSettings;
        if (settings == null || seat < 0 || seat >= settings.Players.Length)
            return FallbackColor;

        return settings.Players[seat].Color;
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
}

#endregion