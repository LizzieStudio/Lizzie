using Godot;

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

    public static readonly GlobalCommand OpenProject = new()
    {
        Name = new("project.open"),
        Icon = CommandIcons.Open,
        Caption = "Open Project",
        SideEffects = _ => _instance?.OpenSampleProject(),
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
            R.Single<ProjectGameSettings>() is var settings
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

    public static readonly GlobalCommand EditPrototypes = new()
    {
        Name = new("editor.prototypes"),
        Icon = CommandIcons.Prototypes,
        Caption = "Prototypes",
        SideEffects = _ => _instance?.ShowPrototypeManifest(),
    };

    public static readonly GlobalCommand EditImages = new()
    {
        Name = new("editor.images"),
        Icon = CommandIcons.Images,
        Caption = "Images",
        SideEffects = _ => _instance?.ShowImageManager(),
    };

    public static readonly GlobalCommand EditProjectSettings = new()
    {
        Name = new("editor.project_settings"),
        Icon = CommandIcons.Settings,
        Caption = "Project Settings",
        SideEffects = _ => _instance?.ShowProjectSettings(),
    };

    /// <summary>Opens the prototype manifest, to place a component from a prototype.</summary>
    public static readonly GlobalCommand InsertExistingComponent = new()
    {
        Name = new("component.insert_existing"),
        Icon = CommandIcons.ExistingComponent,
        Caption = "Existing Component",
        SideEffects = _ => _instance?.ShowPrototypeManifest(),
    };

    public static readonly GlobalCommand InsertNewComponent = new()
    {
        Name = new("component.insert_new"),
        Icon = CommandIcons.NewComponent,
        Caption = "New Component",
        SideEffects = _ => _instance?.ShowComponentDefinition(),
    };
}
