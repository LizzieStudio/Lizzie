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
    private readonly Dictionary<Type, Action<TableEvent>> _handlers = new();

    public override void _Ready()
    {
        _instance = this;
    }

    public void Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : TableEvent
    {
        Action<TableEvent> wrapped = e => handler((TEvent)e);
        _handlers[typeof(TEvent)] = _handlers.TryGetValue(typeof(TEvent), out var existing)
            ? existing + wrapped
            : wrapped;
    }

    public void Clear() => _log.Clear();

    public void Submit(TableEvent e)
    {
        GD.Print($"{Snowport.Clock.source} Submitted event {e.GetType().Name}");

        if (_log.TryRecord(e))
            Dispatch(e);

        if (MultiplayerManager.Instance?.IsMultiplayerActive != true)
            return;

        var json = JsonSerializer.Serialize(e, LizzieJson.Options);

        if (MultiplayerManager.Instance.IsServer)
            Rpc(nameof(ClientReceiveEvent), json);
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

        foreach (var player in MultiplayerManager.Instance.Players)
        {
            if (player.Key == senderId || player.Key == 1)
                continue;
            RpcId(player.Key, nameof(ClientReceiveEvent), json);
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

        var e = JsonSerializer.Deserialize<TableEvent>(json, LizzieJson.Options);
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
        if (_handlers.TryGetValue(e.GetType(), out var handler))
            handler(e);
    }
}
