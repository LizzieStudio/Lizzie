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

    /// <summary>
    /// Clears the log and any open group (without closing it).
    /// </summary>
    public void Clear()
    {
        EventLog.Clear();
        _openGroup = SnowportId.Empty;
    }

    private SnowportId _openGroup = SnowportId.Empty;

    /// <summary>True while the local player's events are being collected into one undo.</summary>
    private bool InGroup => _openGroup != SnowportId.Empty;

    /// <summary>
    /// Closes the open group, if any, with an event that does nothing else.
    /// This is inteded as a fallback.
    /// The proper way to end a group is with `endGroup: true` on <see cref="Submit"/>.
    /// </summary>
    public void EndGroup()
    {
        if (!InGroup)
            return;
        var group = _openGroup;
        _openGroup = SnowportId.Empty;
        Submit(TableEvent.Closing(group));
    }

    /// <summary>
    /// Closes and undoes each gesture <paramref name="source"/> left unfinished.
    /// The host calls this when a player leaves, so it's written once.
    /// </summary>
    public void AbandonGroups(byte source)
    {
        foreach (var group in UndoLog.OpenGroups(EventLog, source))
        {
            Submit(TableEvent.Closing(group));
            Submit(TableEvent.Undoing(new UndoFlag { Reverses = group }));
        }
    }

    /// <summary>
    /// <para>Records, applies, publishes, and sends an event.</para>
    /// <paramref name="startGroup"/> starts attaching all submitted events to one group.
    /// All events in a group are undone and redone together.
    /// This is generally used for gestures, like dragging, which have multiple events.
    /// <paramref name="endGroup"/> stops attaching events to the group <strong>after</strong> this event.
    /// </summary>
    public void Submit(TableEvent e, bool startGroup = false, bool endGroup = false)
    {
        // Currentlly, all events submitted during a drag will be undone with it.
        // For now, this is correct, since it's just things like flip and rotate.
        // This could change in the future, though.
        // TODO: track gestures separately, so events can be in or out of the group.
        if (startGroup)
        {
            // This isn't the worst error, but something went wrong.
            if (InGroup && OS.IsDebugBuild())
                throw new Exception("a gesture left its undo group open");
            EndGroup();
            _openGroup = e.Id;
        }

        // Undos are never grouped, and an event that names its own group keeps it.
        if (InGroup && e.Undo == null && e.Group == SnowportId.Empty)
        {
            e.Group = _openGroup;
            if (endGroup)
            {
                e.Close = true;
                _openGroup = SnowportId.Empty;
            }
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
