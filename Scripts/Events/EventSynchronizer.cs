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

    public readonly OrderedDictionary<SnowportId, TableEvent> EventLog = new();

    /// <summary>
    /// While true, events are recorded but not applied to the scene per-event.
    /// Set during a project load or joining a game.
    /// </summary>
    public bool BulkLoading { get; set; }

    /// <summary>
    /// Represents a player action and its subsequent effects.
    /// </summary>
    public event Action<TableEvent> Applied;

    public override void _EnterTree()
    {
        _instance = this;
    }

    public void Clear()
    {
        EventLog.Clear();
        _openGroup = SnowportId.Empty;
    }

    private SnowportId _openGroup = SnowportId.Empty;

    /// <summary>True while the local player's events are being collected into one undo.</summary>
    public bool InGroup => _openGroup != SnowportId.Empty;

    /// <summary>
    /// Collects the local player's events into one undo.
    /// Stops collecting when an event with <see cref="TableEvent.Close"/> is submitted.
    /// The first event submitted sets the SnowportId of the group.
    /// </summary>
    public void BeginGroup()
    {
        _openGroup = SnowportId.Empty;
    }

    public void Submit(TableEvent e, bool startGroup = false)
    {
        // Currentlly, all events submitted during a drag will be undone with it.
        // For now, this is correct, since it's just things like flip and rotate.
        // This could change in the future, though, and might be incorrect now.
        // TODO: track gestures separately, so events can be in or out of the group.
        if (startGroup)
        {
            if (InGroup)
                throw new Exception("you cannot start a group while one is running");
            _openGroup = e.Id;
        }

        if (InGroup && e.Action is not UndoAction)
        {
            e.Group = _openGroup;

            if (e.Close)
            {
                _openGroup = SnowportId.Empty;
            }
        }
        else
        {
            e.Close = false;
        }

        if (TryRecord(e))
            Applied?.Invoke(e);

        if (MultiplayerManager.Instance?.IsMultiplayerActive != true)
            return;

        var json = JsonSerializer.Serialize(e, LizzieJson.EventOptions);

        if (MultiplayerManager.Instance.IsServer)
            BroadcastEvent(json);
        else
            RpcId(1, nameof(ServerSubmitEvent), json);
    }

    private bool TryRecord(TableEvent e)
    {
        if (EventLog.ContainsKey(e.Id))
            return false;

        // insert to maintain SnowportId order
        // most events arrive in order, so search from the end
        int i = EventLog.Count - 1;
        while (i >= 0 && EventLog.GetAt(i).Key.CompareTo(e.Id) > 0)
            i--;
        EventLog.Insert(i + 1, e.Id, e);
        return true;
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

        if (!TryRecord(e))
            return;

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

        foreach (var e in EventLog.Values)
            RpcId(
                peerId,
                nameof(ReceiveState),
                JsonSerializer.Serialize(e, LizzieJson.EventOptions)
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
