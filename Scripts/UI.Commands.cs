using System.Linq;
using Godot;

/// <summary>
/// The commands of the menu bar, in <see cref="CommandList.MenuBar"/>. Most open one of the UI's dialogs.
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
        Caption = "Project Manager",
        SideEffects = _ => _instance?.ShowProjectManager(),
    };

    public static readonly GlobalCommand OpenProject = new()
    {
        Name = new("project.open"),
        Caption = "Open Project",
        SideEffects = _ => _instance?.OpenSampleProject(),
    };

    /// <summary>Saves the project, asking for a name if it has none.</summary>
    public static readonly GlobalCommand SaveProject = new()
    {
        Name = new("project.save"),
        Caption = "Save Project",
        Keys = [Shortcuts.Ctrl(Key.S)],
        SideEffects = _ => _instance?.Save(),
    };

    public static readonly GlobalCommand CreateSnapshot = new()
    {
        Name = new("snapshot.create"),
        Caption = "Create Snapshot",
        SideEffects = _ => _instance?.ShowSaveSnapshotDialog(),
    };

    /// <summary>Saves the table over the active snapshot.</summary>
    public static readonly GlobalCommand UpdateSnapshot = new()
    {
        Name = new("snapshot.update"),
        Caption = "Update Snapshot",
        Enabled = R => R.Get<GameState>(R.Value<ActiveGameStateRef>().Id) != null,
        Effects = R =>
            ProjectService.Instance.UpdateGameStateEffects(R.Value<ActiveGameStateRef>().Id),
    };

    /// <summary>Offers each snapshot to switch to.</summary>
    public static readonly Submenu RestoreSnapshot = new()
    {
        Caption = "Restore Snapshot",
        Items = R =>
            OrderedGameStates(R)
                .Select(s => new GlobalCommand
                {
                    Name = new("snapshot.restore"),
                    // A caption is a format string, so braces in a name are escaped.
                    Caption = GameStateLabel(R, s.State).Replace("{", "{{").Replace("}", "}}"),
                    Effects = _ => ProjectService.Instance.SwitchGameStateEffects(s.State.Id),
                }),
    };

    public static readonly GlobalCommand ManageSnapshots = new()
    {
        Name = new("snapshot.manage"),
        Caption = "Manage Snapshots...",
        SideEffects = _ => _instance?.ShowSnapshotManager(),
    };

    public static readonly GlobalCommand OpenMultiplayer = new()
    {
        Name = new("app.multiplayer"),
        Caption = "Multiplayer...",
        SideEffects = _ => _instance?.ShowMultiplayerDialog(),
    };

    public static readonly GlobalCommand EditTemplates = new()
    {
        Name = new("editor.templates"),
        Caption = "Templates",
        SideEffects = _ => _instance?.ShowTemplateEditor(),
    };

    public static readonly GlobalCommand EditDatasets = new()
    {
        Name = new("editor.datasets"),
        Caption = "Datasets",
        SideEffects = _ => _instance?.ShowDatasetEditor(),
    };

    public static readonly GlobalCommand EditPrototypes = new()
    {
        Name = new("editor.prototypes"),
        Caption = "Prototype Manifest",
        SideEffects = _ => _instance?.ShowPrototypeManifest(),
    };

    public static readonly GlobalCommand EditImages = new()
    {
        Name = new("editor.images"),
        Caption = "Images",
        SideEffects = _ => _instance?.ShowImageManager(),
    };

    public static readonly GlobalCommand EditProjectSettings = new()
    {
        Name = new("editor.project_settings"),
        Caption = "Project Settings",
        SideEffects = _ => _instance?.ShowProjectSettings(),
    };

    /// <summary>Opens the prototype manifest, to place a component from a prototype.</summary>
    public static readonly GlobalCommand InsertExistingComponent = new()
    {
        Name = new("component.insert_existing"),
        Caption = "Existing Component",
        SideEffects = _ => _instance?.ShowPrototypeManifest(),
    };

    public static readonly GlobalCommand InsertNewComponent = new()
    {
        Name = new("component.insert_new"),
        Caption = "New Component",
        SideEffects = _ => _instance?.ShowComponentDefinition(),
    };
}
