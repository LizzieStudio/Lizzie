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
        EventBus.Instance.Subscribe<DataSetChangedEvent>(OnDataSetChanged);
        EventBus.Instance.Subscribe<TemplateChangedEvent>(OnTemplateChanged);
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

    private void OnDataSetChanged(DataSetChangedEvent evt)
    {
        if (!ShouldSync())
            return;

        GD.Print($"Dataset changed: {evt.DataSetName}");

        var json = ProjectService.Instance.SerializeDataSet(
            ProjectService.Instance.CurrentProject.Datasets[evt.DataSetName]
        );

        if (MultiplayerManager.Instance.IsServer)
            Rpc(nameof(ReceiveDataSetChange), json);
        else
            RpcId(1, nameof(SyncDataSet), json);
    }

    private void OnTemplateChanged(TemplateChangedEvent evt)
    {
        if (!ShouldSync())
            return;

        var templateJson = JsonSerializer.Serialize(evt.Template, LizzieJson.Options);

        if (MultiplayerManager.Instance.IsServer)
            Rpc(nameof(ReceiveTemplate), evt.TemplateName, templateJson);
        else
            RpcId(1, nameof(SyncTemplate), evt.TemplateName, templateJson);
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

            // Prototypes are event-sourced now.
            // Keep whatever this peer already has.
            if (project != null)
                project.Prototypes =
                    ProjectService.Instance.CurrentProject?.Prototypes ?? project.Prototypes;

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

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void SyncDataSet(string dataSetJson)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        ReceiveDataSetChange(dataSetJson);
        Rpc(nameof(ReceiveDataSetChange), dataSetJson);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveDataSetChange(string dataSetJson)
    {
        var dataSet = ProjectService.Instance.DeserializeDataSet(dataSetJson);

        GD.Print($"Receiving dataset change: {dataSet.Name}");
        _isSyncing = true;

        //If the dataset exists, replace it. Otherwise add it.
        ProjectService.Instance.UpdateDataSet(dataSet);

        try
        {
            EventBus.Instance.Publish(new DataSetChangedEvent { DataSetName = dataSet.Name });
        }
        finally
        {
            _isSyncing = false;
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void SyncTemplate(string templateName, string templateJson)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        ReceiveTemplate(templateName, templateJson);
        Rpc(nameof(ReceiveTemplate), templateName, templateJson);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveTemplate(string templateName, string templateJson)
    {
        _isSyncing = true;
        try
        {
            var template = JsonSerializer.Deserialize<Template>(templateJson, LizzieJson.Options);

            if (ProjectService.Instance.CurrentProject.Templates.ContainsKey(templateName))
            {
                ProjectService.Instance.CurrentProject.Templates[templateName] = template;
                EventBus.Instance.Publish(
                    new TemplateChangedEvent { TemplateName = templateName, Template = template }
                );
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to sync template: {ex.Message}");
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

        // Push existing seat assignments so the new client sees who is seated, then
        // replay the player join and leave events so it rebuilds each seat's hand and
        // cursor refs.
        PlayerSeatManager.Instance?.PushSeatMapToClient(senderId);
        EventSynchronizer.Instance?.ReplaySeatEventsTo(senderId);

        // Stream the live table. The snapshot leads with prototype-definition effects,
        // so the joiner registers every prototype before its creation effects, then can spawn.
        // Finally prompt the client to choose its position.
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
