using System;
using System.Collections.Generic;
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

    /// <summary>The recorded events, in SnowportId order. Exposed for debug tooling.</summary>
    public IReadOnlyList<TableEvent> Events => _log.Events;

    /// <summary>
    /// While true, events are recorded but not applied to the scene per-event.
    /// Set during a project load or joining a game.
    /// </summary>
    public bool BulkLoading { get; set; }

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
    /// Server-only: relay an event to every other live peer, skipping the sender
    /// and the host itself.
    /// </summary>
    private void BroadcastEvent(string json, int excludeSender = -1)
    {
        foreach (var player in MultiplayerManager.Instance.Players)
        {
            int id = player.Key;
            if (id == 1 || id == excludeSender)
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
    /// Stream the entire event log to a joining peer, oldest-to-newest.
    /// </summary>
    public void SendStateTo(int peerId)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        var events = _log.Events;
        for (int i = 0; i < events.Count; i++)
            RpcId(
                peerId,
                nameof(ReceiveState),
                JsonSerializer.Serialize(events[i], LizzieJson.EventOptions)
            );
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveState(string json) => ReceiveEvent(json);

    /// <summary>
    /// Ask the host to stream this client the full event log as catchup.
    /// </summary>
    public void RequestCatchup()
    {
        if (MultiplayerManager.Instance?.IsMultiplayerActive != true)
            return;
        if (MultiplayerManager.Instance.IsServer)
            return;

        BulkLoading = true;
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
        GD.Print("[EventSynchronizer] Catchup complete - settling table.");
        ProjectService.Instance?.SettleAfterIngest();
        EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
    }

    #endregion
}
