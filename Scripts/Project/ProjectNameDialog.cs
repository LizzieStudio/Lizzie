using System;
using System.IO;
using Godot;

namespace Lizzie;

/// <summary>
/// Asks for a project's name and folder.
/// </summary>
public partial class ProjectNameDialog : ConfirmationDialog
{
    public delegate void ChosenCallback(string location, string fileName);

    /// <summary>
    /// Called when the user confirms their choice.
    /// </summary>
    public event ChosenCallback OnChosen;

    private LineEdit _name;
    private LineEdit _location;
    private Label _folder;
    private Label _problem;

    /// <summary>
    /// The folder being renamed, or null when it's for a new folder.
    /// </summary>
    private string _renaming;

    /// <summary>
    /// For a new project folder, starting in <paramref name="location"/>.
    /// </summary>
    public static ProjectNameDialog CreateForNew(string title, string okText, string location)
    {
        var dialog = new ProjectNameDialog { Title = title, OkButtonText = okText };
        dialog.Build("", location);
        return dialog;
    }

    /// <summary>
    /// For renaming the project in <paramref name="folder"/>.
    /// </summary>
    public static ProjectNameDialog CreateForRename(string folder)
    {
        var dialog = new ProjectNameDialog
        {
            Title = "Rename Project",
            OkButtonText = "Rename",
            _renaming = folder,
        };
        dialog.Build(ProjectFolder.NameOf(folder), null);
        return dialog;
    }

    private void Build(string name, string location)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(new Label { Text = "Name" });
        _name = new LineEdit { Text = name, PlaceholderText = "My Game" };
        _name.TextChanged += _ => Check();
        box.AddChild(_name);
        RegisterTextEnter(_name);

        if (_renaming == null)
        {
            box.AddChild(new Label { Text = "Location" });
            var row = new HBoxContainer();
            _location = new LineEdit
            {
                Text = location,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            _location.TextChanged += _ => Check();
            row.AddChild(_location);
            var browse = new Button { Text = "Browse..." };
            browse.Pressed += Browse;
            row.AddChild(browse);
            box.AddChild(row);

            _folder = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.Arbitrary,
                Modulate = new Color(1, 1, 1, 0.6f),
            };
            box.AddChild(_folder);
        }

        _problem = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _problem.AddThemeColorOverride("font_color", new Color(1, 0.45f, 0.4f));
        box.AddChild(_problem);
        AddChild(box);

        Confirmed += () => OnChosen?.Invoke(ChosenLocation, ChosenName);
        Confirmed += QueueFree;
        Canceled += QueueFree;
        Check();
    }

    private string ChosenName => _name.Text.Trim();

    private string ChosenLocation =>
        _renaming == null ? _location.Text.Trim() : Path.GetDirectoryName(_renaming);

    /// <summary>
    /// Shows this dialogue.
    /// </summary>
    public void Open()
    {
        PopupCentered();
        _name.GrabFocus();
        _name.SelectAll();
    }

    private string Problem()
    {
        if (ChosenName.Length == 0)
            return "";
        if (_renaming == null)
            return ProjectFolder.ProblemWithLocation(ChosenLocation, ChosenName)
                ?? NestingProblem();

        if (ChosenName == ProjectFolder.NameOf(_renaming))
            return "";
        if (ProjectName.Problem(ChosenName) is { } problem)
            return problem;
        var renamed = Path.Combine(ChosenLocation, ChosenName);
        bool sameFolder = string.Equals(renamed, _renaming, StringComparison.OrdinalIgnoreCase);
        return !sameFolder && ProjectFolder.IsPathTaken(renamed)
            ? $"There's already a \"{ChosenName}\" there."
            : null;
    }

    private string NestingProblem()
    {
        var folder = ProjectFolder.Normalize(Path.Combine(ChosenLocation, ChosenName));
        return ProjectLibrary.NestedWith(folder) is { } project
            ? $"That's inside the project \"{ProjectFolder.NameOf(project)}\"."
            : null;
    }

    private void Check()
    {
        var problem = Problem();
        GetOkButton().Disabled = problem != null;
        _problem.Text = problem ?? "";
        if (_folder != null)
            _folder.Text = problem == null ? Path.Combine(ChosenLocation, ChosenName) : "";
    }

    private void Browse()
    {
        PickFolder(
            this,
            "Choose Where the Project Goes",
            ChosenLocation,
            folder =>
            {
                _location.Text = folder;
                Check();
            }
        );
    }

    /// <summary>
    /// Asks for a folder with the system's folder picker, and calls <paramref name="picked"/> with the one chosen.
    /// </summary>
    /// <param name="host">What the picker opens over.</param>
    /// <param name="startIn">Where the picker starts.</param>
    public static void PickFolder(Node host, string title, string startIn, Action<string> picked)
    {
        var picker = new FileDialog
        {
            Title = title,
            ModeOverridesTitle = false,
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentDir = Directory.Exists(startIn)
                ? startIn
                : OS.GetSystemDir(OS.SystemDir.Documents),
        };
        picker.DirSelected += dir =>
        {
            picker.QueueFree();
            picked(ProjectFolder.Normalize(dir));
        };
        picker.Canceled += picker.QueueFree;
        host.AddChild(picker);
        picker.PopupCentered(new Vector2I(700, 450));
    }
}
