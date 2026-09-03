using System;
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
        Applied?.Invoke(e);
    }
}
