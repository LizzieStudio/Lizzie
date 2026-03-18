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

    public bool IsMultiplayerActive => Multiplayer.HasMultiplayerPeer();
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
        _localPlayerId = Multiplayer.GetUniqueId();

        // Add server as first player
        _players[_localPlayerId] = new PlayerInfo
        {
            PlayerId = _localPlayerId,
            PlayerName = "Host",
            IsLocal = true,
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
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateClient(address, port);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to connect to server: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isServer = false;

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
        _players.Clear();
        _localPlayerId = 0;

        GD.Print("Disconnected from multiplayer");
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");

        var playerId = (int)id;
        _players[playerId] = new PlayerInfo
        {
            PlayerId = playerId,
            PlayerName = $"Player {playerId}",
            IsLocal = false,
        };

        EmitSignal("PlayerConnected", playerId);
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"Peer disconnected: {id}");

        var playerId = (int)id;
        _players.Remove(playerId);

        EmitSignal("PlayerDisconnected", playerId);
    }

    private void OnConnectedToServer()
    {
        _localPlayerId = Multiplayer.GetUniqueId();
        GD.Print($"Connected to server. Local player ID: {_localPlayerId}");

        _players[_localPlayerId] = new PlayerInfo
        {
            PlayerId = _localPlayerId,
            PlayerName = "You",
            IsLocal = true,
        };

        // Register with server
        RpcId(1, nameof(RegisterPlayer), _localPlayerId, _players[_localPlayerId].PlayerName);
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
    private void RegisterPlayer(int playerId, string playerName)
    {
        if (!IsServer)
            return;

        GD.Print($"Player registered: {playerId} - {playerName}");

        if (_players.TryGetValue(playerId, out var player))
        {
            player.PlayerName = playerName;
        }

        // Notify all other players
        Rpc(nameof(UpdatePlayerList), playerId, playerName);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void UpdatePlayerList(int playerId, string playerName)
    {
        if (!_players.ContainsKey(playerId))
        {
            _players[playerId] = new PlayerInfo
            {
                PlayerId = playerId,
                PlayerName = playerName,
                IsLocal = playerId == _localPlayerId,
            };
        }
        else
        {
            _players[playerId].PlayerName = playerName;
        }
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
    public string PlayerName { get; set; }
    public bool IsLocal { get; set; }
}
