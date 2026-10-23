using Godot;

namespace Lizzie;

/// <summary>
/// The commands of the menu bar, whose menus are in the main scene. Most open one of the UI's dialogs.
/// </summary>
public partial class UI
{
    // The UI the commands open dialogs in, while it's in the tree.
    private static UI _instance;

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public static readonly GlobalCommand OpenProjectManager = new()
    {
        Name = new("project.manager"),
        Icon = CommandIcons.ProjectManager,
        Caption = "Project Manager",
        SideEffects = _ => _instance?.ShowProjectManager(),
    };

    /// <summary>Saves the project, asking for a name if it has none.</summary>
    public static readonly GlobalCommand SaveProject = new()
    {
        Name = new("project.save"),
        Icon = CommandIcons.Save,
        Caption = "Save Project",
        Keys = [Shortcuts.Ctrl(Key.S)],
        SideEffects = _ => _instance?.Save(),
    };

    public static readonly GlobalCommand OpenMultiplayer = new()
    {
        Name = new("app.multiplayer"),
        Icon = CommandIcons.Multiplayer,
        Caption = "Multiplayer...",
        SideEffects = _ => _instance?.ShowMultiplayerDialog(),
    };

    /// <summary>
    /// Asks the local player for a seat, or to observe.
    /// </summary>
    public static readonly GlobalCommand ChangeSeat = new()
    {
        Name = new("app.change_seat"),
        Icon = CommandIcons.ChangeSeat,
        Caption = "Change Seat...",
        Enabled = R =>
            R.Get(ProjectGameSettings.Only) is var settings
            && (settings.Players.Length > 0 || settings.AllowObservers),
        SideEffects = _ => _instance?.ShowPlayerPositionDialog(),
    };

    public static readonly GlobalCommand EditTemplates = new()
    {
        Name = new("editor.templates"),
        Icon = CommandIcons.Templates,
        Caption = "Templates",
        SideEffects = _ => _instance?.ShowTemplateEditor(),
    };

    public static readonly GlobalCommand EditDatasets = new()
    {
        Name = new("editor.datasets"),
        Icon = CommandIcons.Datasets,
        Caption = "Datasets",
        SideEffects = _ => _instance?.ShowDatasetEditor(),
    };

    public static readonly GlobalCommand EditComponents = new()
    {
        Name = new("editor.components"),
        Icon = CommandIcons.Components,
        Caption = "Components",
        SideEffects = _ => _instance?.ShowPrototypeManifest(),
    };

    public static readonly GlobalCommand EditImages = new()
    {
        Name = new("editor.images"),
        Icon = CommandIcons.Images,
        Caption = "Images",
        SideEffects = _ => _instance?.ShowImageManager(),
    };

    public static readonly GlobalCommand EditBlueprints = new()
    {
        Name = new("editor.blueprints"),
        Icon = CommandIcons.Blueprints,
        Caption = "Blueprints",
        SideEffects = _ => _instance?.ShowBlueprintManager(),
    };

    public static readonly GlobalCommand EditProjectSettings = new()
    {
        Name = new("editor.project_settings"),
        Icon = CommandIcons.Settings,
        Caption = "Project Settings",
        SideEffects = _ => _instance?.ShowProjectSettings(),
    };

    public static readonly GlobalCommand CreateComponent = new()
    {
        Name = new("editor.create_component"),
        Icon = CommandIcons.AddComponent,
        Caption = "Create New Component",
        SideEffects = _ => _instance?.ShowComponentDefinition(),
    };
}
