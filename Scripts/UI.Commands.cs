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

    [Command("project.manager")]
    public static readonly GlobalCommand OpenProjectManager = new()
    {
        Icon = CommandIcons.ProjectManager,
        Caption = "Project Manager",
        SideEffects = _ => _instance?.ShowProjectManager(),
    };

    /// <summary>Saves the project, asking for a name if it has none.</summary>
    [Command("project.save")]
    public static readonly GlobalCommand SaveProject = new()
    {
        Icon = CommandIcons.Save,
        Caption = "Save Project",
        Keys = [Shortcuts.Ctrl(Key.S)],
        SideEffects = _ => _instance?.Save(),
    };

    [Command("app.multiplayer")]
    public static readonly GlobalCommand OpenMultiplayer = new()
    {
        Icon = CommandIcons.Multiplayer,
        Caption = "Multiplayer...",
        SideEffects = _ => _instance?.ShowMultiplayerDialog(),
    };

    /// <summary>
    /// Asks the local player for a seat, or to observe.
    /// </summary>
    [Command("app.change_seat")]
    public static readonly GlobalCommand ChangeSeat = new()
    {
        Icon = CommandIcons.ChangeSeat,
        Caption = "Change Seat...",
        Enabled = R =>
            R.Get(ProjectGameSettings.Only) is var settings
            && (settings.Players.Length > 0 || settings.AllowObservers),
        SideEffects = _ => _instance?.ShowPlayerPositionDialog(),
    };

    [Command("editor.templates")]
    public static readonly GlobalCommand EditTemplates = new()
    {
        Icon = CommandIcons.Templates,
        Caption = "Templates",
        SideEffects = _ => _instance?.ShowTemplateEditor(),
    };

    [Command("editor.datasets")]
    public static readonly GlobalCommand EditDatasets = new()
    {
        Icon = CommandIcons.Datasets,
        Caption = "Datasets",
        SideEffects = _ => _instance?.ShowDatasetEditor(),
    };

    [Command("editor.components")]
    public static readonly GlobalCommand EditComponents = new()
    {
        Icon = CommandIcons.Components,
        Caption = "Components",
        SideEffects = _ => _instance?.ShowPrototypeManifest(),
    };

    [Command("editor.images")]
    public static readonly GlobalCommand EditImages = new()
    {
        Icon = CommandIcons.Images,
        Caption = "Images",
        SideEffects = _ => _instance?.ShowImageManager(),
    };

    [Command("editor.blueprints")]
    public static readonly GlobalCommand EditBlueprints = new()
    {
        Icon = CommandIcons.Blueprints,
        Caption = "Blueprints",
        SideEffects = _ => _instance?.ShowBlueprintManager(),
    };

    [Command("editor.project_settings")]
    public static readonly GlobalCommand EditProjectSettings = new()
    {
        Icon = CommandIcons.Settings,
        Caption = "Project Settings",
        SideEffects = _ => _instance?.ShowProjectSettings(),
    };

    [Command("editor.create_component")]
    public static readonly GlobalCommand CreateComponent = new()
    {
        Icon = CommandIcons.AddComponent,
        Caption = "Create New Component",
        SideEffects = _ => _instance?.ShowComponentDefinition(),
    };
}
