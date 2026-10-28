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
        Effects = (_, _) =>
        {
            _instance?.ShowProjectManager();
            return null;
        },
    };

    /// <summary>Saves the project, asking for a name if it has none.</summary>
    [Command("project.save")]
    public static readonly GlobalCommand SaveProject = new()
    {
        Icon = CommandIcons.Save,
        Caption = "Save Project",
        Keys = [Shortcuts.Ctrl(Key.S)],
        Effects = (_, _) =>
        {
            _instance?.Save();
            return null;
        },
    };

    [Command("app.multiplayer")]
    public static readonly GlobalCommand OpenMultiplayer = new()
    {
        Icon = CommandIcons.Multiplayer,
        Caption = "Multiplayer...",
        Effects = (_, _) =>
        {
            _instance?.ShowMultiplayerDialog();
            return null;
        },
    };

    /// <summary>
    /// Asks the local player for a seat, or none.
    /// </summary>
    [Command("app.change_seat")]
    public static readonly GlobalCommand ChangeSeat = new()
    {
        Icon = CommandIcons.ChangeSeat,
        Caption = "Change Seat...",
        Effects = (_, _) =>
        {
            _instance?.ShowPlayerSeatingDialog();
            return null;
        },
    };

    [Command("editor.templates")]
    public static readonly GlobalCommand EditTemplates = new()
    {
        Icon = CommandIcons.Templates,
        Caption = "Templates",
        Effects = (_, _) =>
        {
            _instance?.ShowTemplateEditor();
            return null;
        },
    };

    [Command("editor.datasets")]
    public static readonly GlobalCommand EditDatasets = new()
    {
        Icon = CommandIcons.Datasets,
        Caption = "Datasets",
        Effects = (_, _) =>
        {
            _instance?.ShowDatasetEditor();
            return null;
        },
    };

    [Command("editor.components")]
    public static readonly GlobalCommand EditComponents = new()
    {
        Icon = CommandIcons.Components,
        Caption = "Components",
        Effects = (_, _) =>
        {
            _instance?.ShowPrototypeManifest();
            return null;
        },
    };

    [Command("editor.images")]
    public static readonly GlobalCommand EditImages = new()
    {
        Icon = CommandIcons.Images,
        Caption = "Images",
        Effects = (_, _) =>
        {
            _instance?.ShowImageManager();
            return null;
        },
    };

    [Command("editor.blueprints")]
    public static readonly GlobalCommand EditBlueprints = new()
    {
        Icon = CommandIcons.Blueprints,
        Caption = "Blueprints",
        Effects = (_, _) =>
        {
            _instance?.ShowBlueprintManager();
            return null;
        },
    };

    [Command("editor.seats")]
    public static readonly GlobalCommand EditSeats = new()
    {
        Icon = CommandIcons.Seats,
        Caption = "Seats",
        Effects = (_, _) =>
        {
            _instance?.ShowSeats();
            return null;
        },
    };

    [Command("editor.project_settings")]
    public static readonly GlobalCommand EditProjectSettings = new()
    {
        Icon = CommandIcons.Settings,
        Caption = "Project Settings",
        Effects = (_, _) =>
        {
            _instance?.ShowProjectSettings();
            return null;
        },
    };

    [Command("editor.create_component")]
    public static readonly GlobalCommand CreateComponent = new()
    {
        Icon = CommandIcons.AddComponent,
        Caption = "Create New Component",
        Effects = (_, _) =>
        {
            _instance?.ShowComponentDefinition();
            return null;
        },
    };
}
