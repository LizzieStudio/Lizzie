using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Clears the timeline and restarts the game clock.
    /// </summary>
    public void Clear()
    {
        Timeline.Clear();
        SnowStamp.Restart();
    }

    /// <summary>
    /// Cancels each transition <paramref name="source"/> left unfinished.
    /// The host calls this when a player leaves, so it's written once.
    /// </summary>
    public void AbandonTransitions(Source source)
    {
        foreach (var transition in TimelineUndo.OpenTransitions(Timeline, source))
            Submit(new TimelineEvent { Id = SnowStamp.Create(), Ends = transition });
    }

    /// <summary>
    /// Publishes the event to all clients, including this one.
    /// </summary>
    /// <remarks>
    /// Write atoms with <see cref="Atoms.Write(Node, IEnumerable{IChange})"/> and its transition calls instead.
    /// This is for events that aren't plain writes, like undos.
    /// </remarks>
    public void Submit(TimelineEvent e)
    {
        foreach (var atom in e.Writes)
        {
            if (IdQuery.For(atom.GetType()).Of(atom) is IAtomRef { IsNone: true } id)
            {
                throw new ArgumentException(
                    $"A {atom.GetType().Name} was written with an Id that refers to nothing ({id}). "
                        + "Every Ref in an Id needs a tag: make it with Create(), or take it from an existing atom."
                );
            }
        }

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

    /// <summary>
    /// How far ahead of the host's time a player's event may be stamped.
    /// </summary>
    private const long MaxAheadMsec = 10_000;

    /// <summary>
    /// How far behind the host's time a player's event may be stamped.
    /// </summary>
    private const long MaxBehindMsec = 60_000;

    private bool TryRecord(TimelineEvent e)
    {
        if (Timeline.ContainsKey(e.Id))
            return false;

        // insert to maintain SnowStamp order
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
        var e = JsonSerializer.Deserialize<TimelineEvent>(json, LizzieJson.EventOptions);

        // Block events that sent a source ID the host didn't assign to them (this should never happen).
        // I'm considering auto-booting the player when this happens.
        var sender = MultiplayerManager.Instance.Players.GetValueOrDefault(senderId)?.Source;
        if (e == null || e.Id.Source != sender)
        {
            GD.PushWarning(
                $"Dropped an event from peer {senderId} (source {sender?.ToString() ?? "none"}) stamped with source {e?.Id.Source.ToString() ?? "none"}."
            );
            Reject(senderId, e);
            return;
        }

        // Block events stamped too far from the host's time.
        // One far ahead would drag every player's clock forward for good.
        // One far behind would change history.
        long age = e.Id.GetAgeMsec();
        if (age < -MaxAheadMsec || age > MaxBehindMsec)
        {
            GD.PushWarning(
                $"Dropped an event from peer {senderId} stamped {(age < 0 ? $"{-age} ms ahead of" : $"{age} ms behind")} the host's time."
            );
            Reject(senderId, e);
            return;
        }

        Receive(e);
        BroadcastEvent(json, senderId);
    }

    /// <summary>
    /// Server-only: tells the peer that sent <paramref name="e"/> that it was rejected.
    /// That peer then needs to remove that event to stay in-sync.
    /// </summary>
    private void Reject(int senderId, TimelineEvent e)
    {
        if (e != null)
            RpcId(senderId, nameof(ClientEventRejected), (long)e.Id, SnowStamp.Clock);
    }

    /// <summary>
    /// The host calls this on the client whose event it refused.
    /// Drops the event and works everything out again from the rest of the timeline, like joining does.
    /// This should only happen for a bug, a version mismatch, or someone messing with the code.
    /// </summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientEventRejected(long id, long hostClock)
    {
        var rejected = new SnowStamp(id);
        // While joining, the timeline is about to be replaced by the host's anyway.
        if (Joining || !Timeline.ContainsKey(rejected))
            return;

        GD.PushWarning(
            $"The host refused event {rejected}, so it was dropped and the project reloaded."
        );
        var kept = Timeline.Values.Where(e => e.Id != rejected).ToList();
        ProjectService.Instance?.LoadJoinedTimeline(kept);
        SnowStamp.Observe(hostClock);
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

    private void ReceiveEvent(string json) =>
        Receive(JsonSerializer.Deserialize<TimelineEvent>(json, LizzieJson.EventOptions));

    private void Receive(TimelineEvent e)
    {
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
        SnowStamp.Observe(e.Id);

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
        RpcId(senderId, nameof(ClientCatchupComplete), SnowStamp.Clock);
    }

    /// <summary>
    /// Server calls this on the client once all events are synchronized.
    /// </summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientCatchupComplete(long hostClock)
    {
        GD.Print("[EventSynchronizer] Catchup complete - replacing the timeline.");
        var events = _joinBuffer ?? [];
        _joinBuffer = null;
        ProjectService.Instance?.LoadJoinedTimeline(events);
        SnowStamp.Observe(hostClock);
        EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
    }

    #endregion
}
