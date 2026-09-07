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

    private const float MoveEpsilon = 0.1f;

    private const float CursorLift = 0.2f;

    private static Vector3 Miss => DragPlane.Miss;

    private static readonly Color FallbackColor = new(0.8f, 0.8f, 0.8f);

    private DragPlane _dragPlane;
    private Node3D _cursorParent;

    private double _sendAccumulator;
    private Vector3 _lastSentPosition = Miss;

    private Texture2D _cursorTexture;

    private readonly Dictionary<byte, Sprite3D> _cursors = new();

    private readonly Dictionary<byte, Vector3> _positions = new();

    public bool TryGetCursor(byte source, out Vector3 pos) =>
        _positions.TryGetValue(source, out pos);

    private SnowportId _localCursorRef;

    public SnowportId LocalCursorRef
    {
        get
        {
            if (_localCursorRef == SnowportId.Empty)
                _localCursorRef = Snowport.Clock.Create();
            return _localCursorRef;
        }
    }

    public override void _Ready()
    {
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

    private void OnEventApplied(TableEvent e)
    {
        foreach (var effect in e.Effects)
            if (
                effect is UpdatePlayerEffect { HasLeft: false, CursorRef: var cursorRef }
                && cursorRef != SnowportId.Empty
                && cursorRef.source == Snowport.Clock.source
            )
                _localCursorRef = cursorRef;
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
        _localCursorRef = SnowportId.Empty;
        ClearCursors();
    }

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

    private static Color GetSeatColor(byte source)
    {
        var seat = PlayerSeatManager.Instance?.GetSeatBySource(source) ?? -2;
        var settings = ProjectService.Instance?.CurrentProject?.GameSettings;
        if (settings == null || seat < 0 || seat >= settings.Players.Count)
            return FallbackColor;

        var p = settings.Players[seat];
        return new Color(p.ColorR, p.ColorG, p.ColorB, p.ColorA);
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
