using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// Manages multiplayer connections, hosting, and player state
/// </summary>
public partial class MultiplayerManager : Node
{
    private static MultiplayerManager _instance;
    public static MultiplayerManager Instance => _instance;

    private ENetMultiplayerPeer _peer;
    private bool _isServer;
    private bool _isNetworked;
    private int _localPlayerId;
    private Dictionary<int, PlayerInfo> _players = new();

    [Signal]
    public delegate void PlayerConnectedEventHandler(int playerId);

    [Signal]
    public delegate void PlayerDisconnectedEventHandler(int playerId);

    [Signal]
    public delegate void ConnectionFailedEventHandler();

    [Signal]
    public delegate void ServerStartedEventHandler();

    [Signal]
    public delegate void PlayersChangedEventHandler();

    /// <summary>
    /// True when a network session is active.
    /// </summary>
    public bool IsMultiplayerActive => _isNetworked;
    public bool IsServer => _isServer;
    public IReadOnlyDictionary<int, PlayerInfo> Players => _players;

    public override void _EnterTree()
    {
        _instance = this;
    }

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    /// <summary>
    /// Host a local server
    /// </summary>
    public Error HostServer(int port = 7777)
    {
        var error = CreatePeer(peer => peer.CreateServer(port));

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to create server: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isServer = true;
        _isNetworked = true;
        _localPlayerId = Multiplayer.GetUniqueId();

        // Add server as first player
        _players[_localPlayerId] = new PlayerInfo
        {
            PlayerId = _localPlayerId,
            IsLocal = true,
            Source = Source.Host,
        };

        GD.Print($"Server started on port {port}. Server ID: {_localPlayerId}");
        EmitSignal("ServerStarted");

        return Error.Ok;
    }

    /// <summary>
    /// Connect to a server
    /// </summary>
    public Error JoinServer(string address, int port = 7777)
    {
        ProjectService.Instance?.ReplaceWithNewGame();

        var error = CreatePeer(peer => peer.CreateClient(address, port));

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to connect to server: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isServer = false;
        _isNetworked = true;

        EventSynchronizer.Instance?.BeginJoin();

        GD.Print($"Connecting to server at {address}:{port}");

        return Error.Ok;
    }

    /// <summary>
    /// Makes the peer with <paramref name="create"/>, then sets its compression.
    /// We always have to use the same compression on either side, so it's set up in one place.
    /// </summary>
    private Error CreatePeer(Func<ENetMultiplayerPeer, Error> create)
    {
        _peer = new ENetMultiplayerPeer();
        var error = create(_peer);
        if (error != Error.Ok)
            return error;

        // According to Godot, the RangeCoder is best for small packets, and we send a lot of small packets.
        // Later on, we might consider Zstd for catching up a late joiner, but this is fine for now.
        _peer.Host.Compress(ENetConnection.CompressionMode.RangeCoder);
        return Error.Ok;
    }

    /// <summary>
    /// Disconnect from multiplayer
    /// </summary>
    public void Disconnect()
    {
        var wasHost = _isServer;
        var wasGuest = _isNetworked && !_isServer;
        // We effectively need to "disconnect" all peers, even though we're the ones disconnecting.
        var guests = _players
            .Values.Where(p => !p.IsLocal)
            .Select(p => p.Source)
            .OfType<Source>()
            .ToList();

        if (_peer != null)
        {
            _peer.Close();
            _peer = null;
        }

        Multiplayer.MultiplayerPeer = null;
        _isServer = false;
        _isNetworked = false;
        _players.Clear();
        _localPlayerId = 0;
        PresenceSynchronizer.Instance?.ClearCursors();

        if (wasGuest)
        {
            // Leaving someone else's game clears the project.
            EventSynchronizer.Instance?.CancelJoin();
            Source.Me = Source.Host;
            ProjectService.Instance?.ReplaceWithNewGame();
        }
        else if (wasHost)
        {
            // Leaving your own game keeps the project.
            foreach (var source in guests)
                ProjectService.Instance?.AbandonPlayer(source);
            PresenceSynchronizer.Instance?.SeatSoloPlayer();
        }

        GD.Print("Disconnected from multiplayer");
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");

        var playerId = (int)id;
        _players[playerId] = new PlayerInfo { PlayerId = playerId, IsLocal = false };

        EmitSignal("PlayerConnected", playerId);
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"Peer disconnected: {id}");

        var playerId = (int)id;
        _players.Remove(playerId, out var player);

        if (player?.Source is { } source)
        {
            PresenceSynchronizer.Instance?.RemoveCursor(source);
            if (IsServer)
                ProjectService.Instance?.AbandonPlayer(source);
        }

        EmitSignal("PlayerDisconnected", playerId);
    }

    private void OnConnectedToServer()
    {
        _localPlayerId = Multiplayer.GetUniqueId();
        GD.Print($"Connected to server. Local player ID: {_localPlayerId}");

        _players[_localPlayerId] = new PlayerInfo { PlayerId = _localPlayerId, IsLocal = true };

        // Register with the server so it can assign us a source.
        RpcId(1, nameof(RegisterPlayer), _localPlayerId);

        EmitSignal(SignalName.PlayersChanged);
    }

    private void OnConnectionFailed()
    {
        GD.PrintErr("Connection to server failed");
        EmitSignal("ConnectionFailed");
        Disconnect();
    }

    private void OnServerDisconnected()
    {
        GD.Print("Server disconnected");
        Disconnect();
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void RegisterPlayer(int playerId)
    {
        if (!IsServer)
            return;

        if (GetFreeSource() is not { } source)
        {
            // This should never actually happen.
            // Godot's server that we're using only allows 32 peers.
            GD.PrintErr(
                $"Turned away peer {playerId}: no free sources remain, so the game is full."
            );
            _peer.DisconnectPeer(playerId);
            return;
        }
        if (_players.TryGetValue(playerId, out var player))
            player.Source = source;

        GD.Print($"Player registered: peer {playerId} -> source {source}");

        // Give the newcomer its source.
        RpcId(playerId, nameof(AssignSource), source);

        // Give the newcomer the peer to source map of everyone already here.
        foreach (var kv in _players)
            if (kv.Key != playerId && kv.Value.Source is { } other)
                RpcId(playerId, nameof(ReceivePlayerPresence), kv.Key, other);

        // Announce the newcomer to everyone.
        Rpc(nameof(ReceivePlayerPresence), playerId, source);
    }

    /// <summary>
    /// The lowest source no player has, or null when the game is full.
    /// </summary>
    private Source? GetFreeSource()
    {
        var used = _players.Values.Select(p => p.Source).ToHashSet();
        for (int candidate = Source.Host.Value + 1; candidate < Source.Count; candidate++)
            if (!used.Contains(new Source(candidate)))
                return new Source(candidate);
        return null;
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void AssignSource(int source)
    {
        Source.Me = new Source(source);
        PresenceSynchronizer.Instance?.ClearCursors();

        if (_players.TryGetValue(_localPlayerId, out var self))
            self.Source = Source.Me;

        GD.Print($"Assigned source {Source.Me}");
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceivePlayerPresence(int playerId, int source)
    {
        if (!_players.TryGetValue(playerId, out var player))
        {
            player = new PlayerInfo { PlayerId = playerId, IsLocal = playerId == _localPlayerId };
            _players[playerId] = player;
        }

        player.Source = new Source(source);

        EmitSignal(SignalName.PlayersChanged);
    }

    /// <summary>
    /// Check if local player has authority (is server or has permission)
    /// </summary>
    public bool HasAuthority()
    {
        return IsServer || !IsMultiplayerActive;
    }
}

public class PlayerInfo
{
    public int PlayerId { get; set; }
    public bool IsLocal { get; set; }

    /// <summary>
    /// The source the host assigned this player, or null until it has.
    /// </summary>
    public Source? Source { get; set; }
}
