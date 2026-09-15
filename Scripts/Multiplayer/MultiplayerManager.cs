using System;
using System.Collections.Generic;
using Godot;

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
    public int LocalPlayerId => _localPlayerId;
    public IReadOnlyDictionary<int, PlayerInfo> Players => _players;

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

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
    public Error HostServer(int port = 7777, int maxPlayers = 8)
    {
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateServer(port, maxPlayers);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to create server: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isServer = true;
        _isNetworked = true;
        _localPlayerId = Multiplayer.GetUniqueId();

        // Use source 0 by default.
        Snowport.Clock = Snowport.Clock.WithSource(0);

        // Add server as first player
        _players[_localPlayerId] = new PlayerInfo
        {
            PlayerId = _localPlayerId,
            IsLocal = true,
            Source = 0,
        };

        ConnectionStore.Instance?.Clear();

        GD.Print($"Server started on port {port}. Server ID: {_localPlayerId}");
        EmitSignal("ServerStarted");

        return Error.Ok;
    }

    /// <summary>
    /// Connect to a server
    /// </summary>
    public Error JoinServer(string address, int port = 7777)
    {
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateClient(address, port);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to connect to server: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isServer = false;
        _isNetworked = true;

        GD.Print($"Connecting to server at {address}:{port}");

        return Error.Ok;
    }

    /// <summary>
    /// Disconnect from multiplayer
    /// </summary>
    public void Disconnect()
    {
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

        // Start using the host source again.
        Snowport.Clock = Snowport.Clock.WithSource(0);

        // Drop any event history accumulated during the session.
        EventSynchronizer.Instance?.Clear();
        ConnectionStore.Instance?.Clear();

        // Back to solo.
        ConnectionStore.Instance?.EnsureLocalSeat();

        GD.Print("Disconnected from multiplayer");
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");

        var playerId = (int)id;
        _players[playerId] = new PlayerInfo { PlayerId = playerId, IsLocal = false };

        // Withhold live events from the newcomer until it's caught up.
        EventSynchronizer.Instance?.BeginSync(playerId);

        EmitSignal("PlayerConnected", playerId);
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"Peer disconnected: {id}");

        var playerId = (int)id;
        ConnectionStore.Instance?.ReleaseSeat(playerId);
        EventSynchronizer.Instance?.EndSync(playerId);
        _players.Remove(playerId);

        EmitSignal("PlayerDisconnected", playerId);
    }

    private void OnConnectedToServer()
    {
        _localPlayerId = Multiplayer.GetUniqueId();
        GD.Print($"Connected to server. Local player ID: {_localPlayerId}");

        _players[_localPlayerId] = new PlayerInfo { PlayerId = _localPlayerId, IsLocal = true };

        // Register with the server so it can assign us a Snowport source.
        RpcId(1, nameof(RegisterPlayer), _localPlayerId);

        EmitSignal(SignalName.PlayersChanged);

        EventBus.Instance?.Publish<LocalPlayerJoinedGameEvent>();
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

        var source = GetSnowportSource();
        if (_players.TryGetValue(playerId, out var player))
            player.Source = source;

        GD.Print($"Player registered: peer {playerId} -> source {source}");

        // Give the newcomer its Snowport source.
        RpcId(playerId, nameof(AssignSource), source);

        // Give the newcomer the peer to source map of everyone already here.
        foreach (var kv in _players)
            if (kv.Key != playerId)
                RpcId(playerId, nameof(ReceivePlayerPresence), kv.Key, (int)kv.Value.Source);

        // Announce the newcomer to everyone.
        Rpc(nameof(ReceivePlayerPresence), playerId, (int)source);
    }

    /// <summary>
    /// Get the lowest unclaimed source number for a SnowportId and SnowTag.
    /// </summary>
    private byte GetSnowportSource()
    {
        var used = new HashSet<byte>();
        foreach (var p in _players.Values)
            used.Add(p.Source);

        for (byte candidate = 1; candidate <= byte.MaxValue; candidate++)
        {
            if (!used.Contains(candidate))
                return candidate;
        }

        GD.PrintErr("No free Snowport source ids remain; table is full.");
        return 1;
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void AssignSource(int source)
    {
        var assigned = (byte)source;
        Snowport.Clock = new Snowport(assigned);

        if (_players.TryGetValue(_localPlayerId, out var self))
            self.Source = assigned;

        GD.Print($"Assigned Snowport source {assigned}");
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

        player.Source = (byte)source;

        EmitSignal(SignalName.PlayersChanged);
    }

    /// <summary>
    /// Check if local player has authority (is server or has permission)
    /// </summary>
    public bool HasAuthority()
    {
        return IsServer || !IsMultiplayerActive;
    }

    /// <summary>
    /// Check if a specific peer has authority
    /// </summary>
    public bool PeerHasAuthority(int peerId)
    {
        return peerId == 1 || !IsMultiplayerActive; // Server (peer 1) always has authority
    }
}

public class PlayerInfo
{
    public int PlayerId { get; set; }
    public bool IsLocal { get; set; }

    /// <summary>The Snowport source id assigned to this player by the server.</summary>
    public byte Source { get; set; }
}
