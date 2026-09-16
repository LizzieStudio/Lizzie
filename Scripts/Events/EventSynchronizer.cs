using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

/// <summary>
/// The event-sourced synchronizer. Streams events to all peers.
/// </summary>
public partial class EventSynchronizer : Node
{
    private static EventSynchronizer _instance;
    public static EventSynchronizer Instance => _instance;

    private readonly EventLog _log = new();

    /// <summary>The recorded events, in arrival order. Exposed for debug tooling.</summary>
    public IReadOnlyList<TableEvent> Events => _log.Events;

    /// <summary>
    /// Peers still receiving their catchup events.
    /// They are excluded from live event delivery until caught up.
    /// </summary>
    private readonly HashSet<int> _syncingPeers = new();

    /// <summary>
    /// Represents a player action and its subsequent effects.
    /// </summary>
    public event Action<TableEvent> Applied;

    public override void _Ready()
    {
        _instance = this;
    }

    public void Clear() => _log.Clear();

    public void Submit(TableEvent e)
    {
        GD.Print(
            $"{Snowport.Clock.source} Submitted event action={e.Action?.GetType().Name ?? "none"} effects={e.Effects.Length}"
        );

        if (_log.TryRecord(e))
            Dispatch(e);

        if (MultiplayerManager.Instance?.IsMultiplayerActive != true)
            return;

        var json = JsonSerializer.Serialize(e, LizzieJson.EventOptions);

        if (MultiplayerManager.Instance.IsServer)
            BroadcastEvent(json);
        else
            RpcId(1, nameof(ServerSubmitEvent), json);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ServerSubmitEvent(string json)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        ReceiveEvent(json);
        BroadcastEvent(json, senderId);
    }

    /// <summary>
    /// Server-only: relay an event to every other live peer, skipping the sender,
    /// the host itself, and any peer still receiving its initial snapshot.
    /// </summary>
    private void BroadcastEvent(string json, int excludeSender = -1)
    {
        foreach (var player in MultiplayerManager.Instance.Players)
        {
            int id = player.Key;
            if (id == 1 || id == excludeSender || _syncingPeers.Contains(id))
                continue;
            RpcId(id, nameof(ClientReceiveEvent), json);
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientReceiveEvent(string json)
    {
        ReceiveEvent(json);
    }

    private void ReceiveEvent(string json)
    {
        GD.Print($"{Snowport.Clock.source} Received event {json}");

        var e = JsonSerializer.Deserialize<TableEvent>(json, LizzieJson.EventOptions);
        Ingest(e);
    }

    /// <summary>
    /// Record and apply an event locally without broadcasting it.
    /// </summary>
    public void Ingest(TableEvent e)
    {
        if (e == null)
            return;

        // Advance the hybrid clock
        Snowport.Clock.Process(e.Id);

        if (!_log.TryRecord(e))
            return;

        Dispatch(e);
    }

    private void Dispatch(TableEvent e)
    {
        Applied?.Invoke(e);
    }

    #region Catchup

    /// <summary>
    /// Mark a freshly connected peer as syncing so it is excluded from
    /// live event delivery until it has received its state snapshot.
    /// </summary>
    public void BeginSync(int peerId)
    {
        if (MultiplayerManager.Instance?.IsServer == true)
            _syncingPeers.Add(peerId);
    }

    /// <summary>
    /// Stop excluding a peer from live event delivery.
    /// </summary>
    public void EndSync(int peerId) => _syncingPeers.Remove(peerId);

    /// <summary>
    /// The effects that persist in a saved project.
    /// </summary>
    public Effect[] BuildProjectEffects() =>
        (TemplateStore.Instance?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .Concat(DataSetStore.Instance?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .Concat(PrototypeStore.Instance?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .Concat(AssetStore.Instance?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .Concat(GameStatesStore.Instance?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .Concat(SettingsManager.Instance?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .ToArray();

    /// <summary>
    /// Stream the current table to a joining peer as fresh creation events.
    public void SendStateTo(int peerId)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        var gameObjects = ProjectService.Instance?.GameObjects;
        if (gameObjects == null)
        {
            EndSync(peerId);
            return;
        }

        int cutoff = _log.Count;
        var effects = gameObjects
            .GenerateCatchupEffects()
            .Concat(ConnectionStore.Instance?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .Concat(BuildProjectEffects())
            .ToArray();
        var snapshot = TableEvent.Now(null, effects);
        RpcId(
            peerId,
            nameof(ReceiveSnapshot),
            JsonSerializer.Serialize(snapshot, LizzieJson.EventOptions)
        );

        // Replay anything that landed after the cutoff.
        foreach (var e in _log.EventsFrom(cutoff))
            RpcId(
                peerId,
                nameof(ReceiveBacklog),
                JsonSerializer.Serialize(e, LizzieJson.EventOptions)
            );

        // The peer is caught up. Resume sending them events.
        EndSync(peerId);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveSnapshot(string json) => ReceiveEvent(json);

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveBacklog(string json) => ReceiveEvent(json);

    /// <summary>
    /// Ask the host to stream us the current table as catchup events.
    /// </summary>
    public void RequestCatchup()
    {
        if (MultiplayerManager.Instance?.IsMultiplayerActive != true)
            return;
        if (MultiplayerManager.Instance.IsServer)
            return;

        RpcId(1, nameof(ServerSendCatchup));
    }

    /// <summary>
    /// Client calls this on the server to request "catch-up" events.
    /// </summary>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ServerSendCatchup()
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        SendStateTo(senderId);
        RpcId(senderId, nameof(ClientCatchupComplete));
    }

    /// <summary>
    /// Server calls this on the client once all events are synchronized.
    /// </summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientCatchupComplete()
    {
        GD.Print("[EventSynchronizer] Catchup complete – requesting player position selection.");
        EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
    }

    #endregion
}
