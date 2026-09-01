using System.Collections.Generic;
using Godot;

/// <summary>
/// Streams each client's cursor position in multiplayer.
/// </summary>
public partial class CursorSynchronizer : Node
{
    private static CursorSynchronizer _instance;
    public static CursorSynchronizer Instance => _instance;

    private const string CursorTexturePath = "res://Textures/cursor.png";

    private const double SendInterval = 1.0 / 30.0;

    /// <summary>Minimum world-space movement before a new position is sent.</summary>
    private const float MoveEpsilon = 0.1f;

    /// <summary>Height above the table (drag plane) at which cursor sprites float.</summary>
    private const float CursorLift = 0.2f;

    /// <summary>The value returned by DragPlane.GetCursorProjection() on a ray miss.</summary>
    private static readonly Vector3 Miss = new(-99, -99, -99);

    private static readonly Color FallbackColor = new(0.8f, 0.8f, 0.8f);

    private DragPlane _dragPlane;
    private Node3D _cursorParent;

    private double _sendAccumulator;
    private Vector3 _lastSentPosition = Miss;

    private Texture2D _cursorTexture;
    private readonly Dictionary<int, Sprite3D> _cursors = new();

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

    public override void _Process(double delta)
    {
        var mm = MultiplayerManager.Instance;
        if (_dragPlane == null || mm?.IsMultiplayerActive != true)
            return;

        RemoveStaleCursors(mm);

        _sendAccumulator += delta;
        if (_sendAccumulator < SendInterval)
            return;
        _sendAccumulator = 0;

        var pos = _dragPlane.GetCursorProjection();
        if (pos == Miss)
            return;
        if (_lastSentPosition != Miss && pos.DistanceTo(_lastSentPosition) < MoveEpsilon)
            return;
        _lastSentPosition = pos;

        if (mm.IsServer)
            Rpc(nameof(ClientReceiveCursor), mm.LocalPlayerId, pos);
        else
            RpcId(1, nameof(ServerReceiveCursor), pos);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered
    )]
    private void ServerReceiveCursor(Vector3 pos)
    {
        var mm = MultiplayerManager.Instance;
        if (mm?.IsServer != true)
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        UpdateCursor(senderId, pos);

        foreach (var player in mm.Players)
        {
            if (player.Key == senderId || player.Key == 1)
                continue;
            RpcId(player.Key, nameof(ClientReceiveCursor), senderId, pos);
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered
    )]
    private void ClientReceiveCursor(int peerId, Vector3 pos)
    {
        UpdateCursor(peerId, pos);
    }

    private void UpdateCursor(int peerId, Vector3 pos)
    {
        if (_cursorParent == null)
            return;
        if (peerId == MultiplayerManager.Instance?.LocalPlayerId)
            return;

        if (!_cursors.TryGetValue(peerId, out var sprite))
        {
            sprite = CreateCursorSprite();
            _cursorParent.AddChild(sprite);
            _cursors[peerId] = sprite;
        }

        sprite.Modulate = GetSeatColor(peerId);
        sprite.Position = pos + Vector3.Up * CursorLift;
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

    private static Color GetSeatColor(int peerId)
    {
        var seat = PlayerSeatManager.Instance?.GetSeat(peerId) ?? -2;
        var settings = ProjectService.Instance?.CurrentProject?.GameSettings;
        if (settings == null || seat < 0 || seat >= settings.Players.Count)
            return FallbackColor;

        var p = settings.Players[seat];
        return new Color(p.ColorR, p.ColorG, p.ColorB, p.ColorA);
    }

    private void RemoveStaleCursors(MultiplayerManager mm)
    {
        if (_cursors.Count == 0)
            return;

        List<int> stale = null;
        foreach (var peerId in _cursors.Keys)
        {
            if (!mm.Players.ContainsKey(peerId))
                (stale ??= new List<int>()).Add(peerId);
        }

        if (stale == null)
            return;

        foreach (var peerId in stale)
        {
            if (_cursors.TryGetValue(peerId, out var sprite))
                sprite.QueueFree();
            _cursors.Remove(peerId);
        }
    }

    private void ClearCursors()
    {
        foreach (var sprite in _cursors.Values)
            sprite.QueueFree();
        _cursors.Clear();
    }
}
