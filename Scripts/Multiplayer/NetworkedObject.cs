using System;
using System.Text.Json;
using Godot;

/// <summary>
/// Makes a VisualComponentBase network-synchronized
/// </summary>
public partial class NetworkedObject : Node
{
    [Export]
    public VisualComponentBase Component { get; set; }

    private int _lockedByPlayer = 0; // 0 = unlocked, otherwise player ID
    private Vector3 _lastSyncedPosition;
    private Vector3 _lastSyncedRotation;
    private int _syncFrameCounter = 0;
    private const int SyncInterval = 3; // Sync every N frames when dragging

    public bool IsLockedByAnotherPlayer =>
        _lockedByPlayer > 0 && _lockedByPlayer != MultiplayerManager.Instance?.LocalPlayerId;

    public int LockedByPlayer => _lockedByPlayer;

    public override void _Ready()
    {
        if (Component == null)
        {
            Component = GetParent<VisualComponentBase>();
        }

        if (Component == null)
        {
            GD.PrintErr("NetworkedObject requires a VisualComponentBase parent");
            return;
        }

        // Set multiplayer authority
        if (MultiplayerManager.Instance?.IsServer == true)
        {
            SetMultiplayerAuthority(1); // Server has authority
        }
    }

    public override void _Process(double delta)
    {
        if (!MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return;
        if (Component == null)
            return;

        // If this object is being dragged by local player, sync periodically
        if (Component.IsDragging) //&& _lockedByPlayer == MultiplayerManager.Instance.LocalPlayerId)
        {
            _syncFrameCounter++;
            if (_syncFrameCounter >= SyncInterval)
            {
                if (
                    Component.Position.DistanceTo(_lastSyncedPosition) > 0.01f
                    || Component.Rotation.DistanceTo(_lastSyncedRotation) > 0.01f
                )
                {
                    _lastSyncedPosition = Component.Position;
                    _lastSyncedRotation = Component.Rotation;
                    EventBus.Instance.Publish(new SyncTransformEvent { Component = Component });
                }

                _syncFrameCounter = 0;
            }
        }
    }

    /// <summary>
    /// Attempt to lock this object for dragging
    /// </summary>
    public bool TryLock()
    {
        if (!MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return true; // Allow in single player

        if (_lockedByPlayer == MultiplayerManager.Instance.LocalPlayerId)
            return true; // Already locked by us

        if (_lockedByPlayer > 0)
            return false; // Locked by another player

        // Request lock from server
        RequestLock();
        return false; // Will be async
    }

    /// <summary>
    /// Release the lock on this object
    /// </summary>
    public void Unlock()
    {
        if (!MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return;

        if (_lockedByPlayer == MultiplayerManager.Instance.LocalPlayerId)
        {
            RequestUnlock();
        }
    }

    private void RequestLock()
    {
        RpcId(1, nameof(ServerRequestLock), MultiplayerManager.Instance.LocalPlayerId);
    }

    private void RequestUnlock()
    {
        RpcId(1, nameof(ServerRequestUnlock), MultiplayerManager.Instance.LocalPlayerId);
        _lockedByPlayer = 0;
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ServerRequestLock(int playerId)
    {
        if (!MultiplayerManager.Instance?.IsServer == true)
            return;

        if (_lockedByPlayer == 0 || _lockedByPlayer == playerId)
        {
            _lockedByPlayer = playerId;
            // Notify all clients
            Rpc(nameof(ClientReceiveLock), playerId);
        }
        else
        {
            // Lock denied
            RpcId(playerId, nameof(ClientLockDenied));
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ServerRequestUnlock(int playerId)
    {
        if (!MultiplayerManager.Instance?.IsServer == true)
            return;

        if (_lockedByPlayer == playerId)
        {
            _lockedByPlayer = 0;
            // Notify all clients
            Rpc(nameof(ClientReceiveUnlock));
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientReceiveLock(int playerId)
    {
        _lockedByPlayer = playerId;

        if (playerId == MultiplayerManager.Instance?.LocalPlayerId)
        {
            // Lock granted for us
            GD.Print($"Lock granted for object: {Component?.ComponentName}");
        }
        else
        {
            // Another player locked this object
            if (Component != null && Component.IsDragging)
            {
                // Force end drag if we were dragging
                Component.IsDragging = false;
            }
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientReceiveUnlock()
    {
        _lockedByPlayer = 0;
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientLockDenied()
    {
        GD.Print($"Lock denied for object: {Component?.ComponentName}");

        if (Component != null)
        {
            Component.IsDragging = false;
        }
    }
}
