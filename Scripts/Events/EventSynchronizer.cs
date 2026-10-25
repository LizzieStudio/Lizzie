using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// The event-sourced synchronizer. Streams events to all peers.
/// </summary>
public partial class EventSynchronizer : Node
{
    private static EventSynchronizer _instance;
    public static EventSynchronizer Instance => _instance;

    public readonly Timeline Timeline = new();

    /// <summary>
    /// While true, events are recorded but not applied to the scene per-event.
    /// Set during a project load, including the host's timeline after joining a game.
    /// </summary>
    public bool BulkLoading { get; set; }

    /// <summary>
    /// Raised for each event recorded, from this player, another player, or a load.
    /// </summary>
    public event Action<TimelineEvent> Applied;

    public override void _EnterTree()
    {
        _instance = this;
    }

    /// <summary>
    /// Clears the timeline.
    /// </summary>
    public void Clear()
    {
        Timeline.Clear();
    }

    /// <summary>
    /// Cancels each gesture <paramref name="source"/> left unfinished.
    /// The host calls this when a player leaves, so it's written once.
    /// </summary>
    public void AbandonGroups(byte source)
    {
        foreach (var group in TimelineUndo.OpenGroups(Timeline, source))
            Submit(TimelineEvent.Canceling(group));
    }

    /// <summary>
    /// Publishes the event to all clients, including this one.
    /// </summary>
    /// <remarks>
    /// Write atoms with <see cref="AtomService.Write(Node, IEnumerable{IChange})"/> and its gesture calls instead.
    /// This is for events that aren't plain writes, like undos.
    /// </remarks>
    public void Submit(TimelineEvent e)
    {
        foreach (var atom in e.Writes)
            if (IdQuery.For(atom.GetType()).Of(atom) is SnowTag id && id == SnowTag.Empty)
                throw new ArgumentException(
                    $"A {atom.GetType().Name} was written without an Id. Give it SnowTag.Create() when it's made."
                );

        // The timeline is about to be replaced by the host's, so a change now would be lost.
        if (Joining)
        {
            GD.PushWarning($"Ignored a change made while joining a game: {e.Command}");
            return;
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

    private bool TryRecord(TimelineEvent e)
    {
        if (Timeline.ContainsKey(e.Id))
            return false;

        // insert to maintain SnowportId order
        // most events arrive in order, so search from the end
        int i = Timeline.Count - 1;
        while (i >= 0 && Timeline.GetAt(i).Key.CompareTo(e.Id) > 0)
            i--;
        Timeline.Insert(i + 1, e.Id, e);
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
        var e = JsonSerializer.Deserialize<TimelineEvent>(json, LizzieJson.EventOptions);
        if (Joining)
            _joinBuffer.Add(e);
        else
            Ingest(e);
    }

    /// <summary>
    /// Record and apply an event locally without broadcasting it.
    /// </summary>
    public void Ingest(TimelineEvent e)
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

    // the host's events, collected while joining, or null when not joining
    private List<TimelineEvent> _joinBuffer;

    /// <summary>
    /// True while connecting to a game.
    /// </summary>
    public bool Joining => _joinBuffer != null;

    /// <summary>
    /// Starts collecting the host's events instead of applying them.
    /// </summary>
    public void BeginJoin() => _joinBuffer = new();

    /// <summary>
    /// Drops the collected events, for when a join fails or is abandoned.
    /// </summary>
    public void CancelJoin() => _joinBuffer = null;

    /// <summary>
    /// Stream the entire timeline to a joining peer, oldest-to-newest.
    /// </summary>
    private void SendStateTo(int peerId)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        foreach (var e in Timeline.Values)
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
    /// Ask the host to stream this client the full timeline as catchup.
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
        GD.Print("[EventSynchronizer] Catchup complete - replacing the timeline.");
        var events = _joinBuffer ?? [];
        _joinBuffer = null;
        ProjectService.Instance?.LoadJoinedTimeline(events);
        EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
    }

    #endregion
}
