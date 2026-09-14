using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>
/// Synchronizes project data across multiplayer sessions
/// </summary>
public partial class ProjectSynchronizer : Node
{
    private static ProjectSynchronizer _instance;
    public static ProjectSynchronizer Instance => _instance;

    // Prevents EventBus re-triggering a sync when we are applying an incoming network update
    private bool _isSyncing = false;

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

        // Subscribe to project changes
        EventBus.Instance.Subscribe<ProjectChangedEvent>(OnProjectChanged);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void OnProjectChanged(ProjectChangedEvent evt)
    {
        if (!ShouldSync())
            return;

        var projectJson = ProjectService.Instance.SerializeProject(
            ProjectService.Instance.CurrentProject
        );

        if (MultiplayerManager.Instance.IsServer)
            Rpc(nameof(ReceiveProject), projectJson);
        else
            RpcId(1, nameof(SyncProject), projectJson);
    }

    private bool ShouldSync()
    {
        return MultiplayerManager.Instance?.IsMultiplayerActive == true && !_isSyncing;
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void SyncProject(string projectJson)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        ReceiveProject(projectJson);
        Rpc(nameof(ReceiveProject), projectJson);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveProject(string projectJson)
    {
        GD.Print("Receiving project sync");
        _isSyncing = true;
        try
        {
            var project = ProjectService.Instance.DeserializeProject(projectJson);

            // Prototypes and templates are event-sourced now.
            // Keep whatever this peer already has.
            if (project != null)
            {
                project.Prototypes =
                    ProjectService.Instance.CurrentProject?.Prototypes ?? project.Prototypes;
                project.Templates =
                    ProjectService.Instance.CurrentProject?.Templates ?? project.Templates;
                project.Datasets =
                    ProjectService.Instance.CurrentProject?.Datasets ?? project.Datasets;
                project.Images =
                    ProjectService.Instance.CurrentProject?.Images ?? project.Images;
                project.GameSettings =
                    ProjectService.Instance.CurrentProject?.GameSettings ?? project.GameSettings;
            }

            ProjectService.Instance.SetProjectSilent(project);
            EventBus.Instance.Publish<ProjectChangedEvent>();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to deserialize project: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>
    /// Request full project sync from server (called by new clients)
    /// </summary>
    public void RequestProjectSync()
    {
        if (!MultiplayerManager.Instance?.IsMultiplayerActive == true)
            return;
        if (MultiplayerManager.Instance.IsServer)
            return;

        RpcId(1, nameof(RequestFullSync));
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void RequestFullSync()
    {
        if (!MultiplayerManager.Instance?.IsServer == true)
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        var projectJson = ProjectService.Instance.SerializeProject(
            ProjectService.Instance.CurrentProject
        );

        RpcId(senderId, nameof(ReceiveProject), projectJson);

        EventSynchronizer.Instance?.SendStateTo(senderId);
        RpcId(senderId, nameof(NotifyClientProjectReady));
    }

    /// <summary>
    /// Called on the client after the server has sent the full project and the seat map.
    /// Prompts the local player to pick their position.
    /// </summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void NotifyClientProjectReady()
    {
        GD.Print("[ProjectSynchronizer] Project ready – requesting player position selection.");
        EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
    }
}
