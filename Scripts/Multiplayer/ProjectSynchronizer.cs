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
        EventBus.Instance.Subscribe<PrototypeChangedEvent>(OnPrototypeChanged);
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

        // Serialize and sync entire project
        var projectJson = ProjectService.Instance.SerializeProject(
            ProjectService.Instance.CurrentProject
        );
        RpcId(1, nameof(SyncProject), projectJson);
    }

    private void OnDataSetChanged(DataSetChangedEvent evt)
    {
        if (!ShouldSync())
            return;

        GD.Print($"Dataset changed: {evt.DataSetName}");
        RpcId(1, nameof(SyncDataSet), evt.DataSetName);
    }

    private void OnPrototypeChanged(PrototypeChangedEvent evt)
    {
        if (!ShouldSync())
            return;

        var prototype = ProjectService.Instance.CurrentProject.Prototypes[evt.PrototypeId];
        var prototypeJson = JsonSerializer.Serialize(prototype);

        RpcId(1, nameof(SyncPrototype), evt.PrototypeId.ToString(), prototypeJson);
    }

    private void OnTemplateChanged(TemplateChangedEvent evt)
    {
        if (!ShouldSync())
            return;

        var templateJson = JsonSerializer.Serialize(evt.Template);
        RpcId(1, nameof(SyncTemplate), evt.TemplateName, templateJson);
    }

    private bool ShouldSync()
    {
        return MultiplayerManager.Instance?.IsMultiplayerActive == true;
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

        // Server received project update, broadcast to all clients
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

        try
        {
            var project = ProjectService.Instance.DeserializeProject(projectJson);
            ProjectService.Instance.SetProjectSilent(project);

            // Publish event to refresh GameObjects
            EventBus.Instance.Publish<ProjectChangedEvent>();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to deserialize project: {ex.Message}");
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void SyncDataSet(string dataSetName)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        // Broadcast to all clients
        Rpc(nameof(ReceiveDataSetChange), dataSetName);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveDataSetChange(string dataSetName)
    {
        GD.Print($"Receiving dataset change: {dataSetName}");
        EventBus.Instance.Publish(new DataSetChangedEvent { DataSetName = dataSetName });
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void SyncPrototype(string prototypeIdStr, string prototypeJson)
    {
        if (MultiplayerManager.Instance?.IsServer != true)
            return;

        // Broadcast to all clients
        Rpc(nameof(ReceivePrototype), prototypeIdStr, prototypeJson);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceivePrototype(string prototypeIdStr, string prototypeJson)
    {
        try
        {
            var prototypeId = Guid.Parse(prototypeIdStr);
            var prototype = JsonSerializer.Deserialize<Prototype>(prototypeJson);

            if (ProjectService.Instance.CurrentProject.Prototypes.ContainsKey(prototypeId))
            {
                ProjectService.Instance.CurrentProject.Prototypes[prototypeId] = prototype;
                EventBus.Instance.Publish(new PrototypeChangedEvent { PrototypeId = prototypeId });
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to sync prototype: {ex.Message}");
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

        // Broadcast to all clients
        Rpc(nameof(ReceiveTemplate), templateName, templateJson);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ReceiveTemplate(string templateName, string templateJson)
    {
        try
        {
            var template = JsonSerializer.Deserialize<Template>(templateJson);

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
    }
}
