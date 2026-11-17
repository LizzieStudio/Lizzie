using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SaveUpgrades;

namespace Lizzie;

// I didn't use Atoms for this, since the details shouldn't be shared in multiplayer
// or stored in the projects timeline. It also should be able to run in a main menu,
// once we get around to that.
public partial class ProjectManager : Window
{
    /// <summary>
    /// Asks to open the project in the folder.
    /// The open project has to close first, which may ask the player things, so the UI does it.
    /// </summary>
    public event Action<string> OpenRequested;

    private static readonly Texture2D WarningIcon = GD.Load<Texture2D>(CommandIcons.Warning);

    private ItemList _projects;
    private Button _open;
    private Button _rename;
    private Button _delete;
    private Button _showInFolder;

    // The listed projects, in the list's order.
    private List<ProjectStatus> _statuses = [];

    public override void _Ready()
    {
        _projects = GetNode<ItemList>("%Projects");
        _projects.ItemSelected += _ => UpdateButtons();
        _projects.EmptyClicked += (_, button) =>
        {
            if (button != (long)MouseButton.Left)
                return;
            _projects.DeselectAll();
            UpdateButtons();
        };
        _projects.ItemActivated += _ => OpenProject();
        _projects.GuiInput += OnProjectsInput;

        GetNode<Button>("%New").Pressed += CreateNewProject;
        _open = GetNode<Button>("%Open");
        _open.Pressed += OpenProject;
        _rename = GetNode<Button>("%Rename");
        _rename.Pressed += RenameProject;
        _delete = GetNode<Button>("%Delete");
        _delete.Pressed += DeleteProject;
        GetNode<Button>("%AddExisting").Pressed += AddExistingProject;
        _showInFolder = GetNode<Button>("%ShowInFolder");
        _showInFolder.Pressed += ShowInFolder;

        FocusEntered += RefreshLater;
        CloseRequested += QueueFree;

        Refresh(ProjectService.Instance?.CurrentProject?.Folder);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn)
            RefreshLater();
    }

    private void RefreshLater() => Callable.From(() => Refresh()).CallDeferred();

    private static string OpenFolder => ProjectService.Instance?.CurrentProject?.Folder;

    private ProjectStatus Selected =>
        _projects.GetSelectedItems() is [var index] ? _statuses[index] : null;

    private void Refresh(string select = null)
    {
        select ??= Selected?.Folder;
        _projects.Clear();
        _statuses = ProjectLibrary.Folders.Select(ProjectStatus.Of).ToList();

        var dim = _projects.GetThemeColor("font_color") with { A = 0.5f };
        foreach (var status in _statuses)
        {
            var warning = Check(status);
            int index = _projects.AddItem(status.Name, warning == null ? null : WarningIcon);
            _projects.SetItemTooltip(
                index,
                warning == null ? status.Folder : $"{status.Folder}\n\n{warning}"
            );
            if (!status.CanOpen)
                _projects.SetItemCustomFgColor(index, dim);
            if (status.Folder == select)
                _projects.Select(index);
        }
        UpdateButtons();
    }

    /// <summary>
    /// Checks the project and returns a warning or null if it's fine.
    /// </summary>
    private static string Check(ProjectStatus status)
    {
        if (!status.Found)
            return "Its folder can't be found. It may have been moved, renamed or deleted, "
                + "or be on a drive that isn't connected.";
        if (!status.CanOpen)
            return "It was saved by a newer version of Lizzie. Update Lizzie to open it.";
        if (status.OpensOlderSave)
            return "It was saved by a newer version of Lizzie. This opens its older save, "
                + "and changes saved here won't appear in the newer version.";
        return null;
    }

    private void UpdateButtons()
    {
        var selected = Selected;
        var cantOpen = selected switch
        {
            null => "Choose a project to open.",
            { Found: false } => "Its folder can't be found.",
            { CanOpen: false } => "It was saved by a newer version of Lizzie.",
            _ when selected.Folder == OpenFolder => "It's open already.",
            _ => null,
        };
        _open.Disabled = cantOpen != null;
        _open.TooltipText = cantOpen ?? "Open the project. (Enter or double-click)";

        _rename.Disabled = selected is not { Found: true };
        _showInFolder.Disabled = selected is not { Found: true };

        // This is for when a project goes missing outside of Lizzie.
        // We still have it on the list, but all the user can do is remove it.
        bool missing = selected is { Found: false };
        _delete.Disabled = selected == null;
        _delete.Text = missing ? "Remove from List" : "Delete";
        _delete.TooltipText = missing
            ? "Remove the missing project from the list. (Delete)"
            : "Move the project's folder to the trash. (Delete)";
    }

    private void OnProjectsInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode == Key.Delete)
            DeleteProject();
        else if (key.Keycode == Key.F2)
            RenameProject();
        else
            return;
        _projects.AcceptEvent();
    }

    private void CreateNewProject()
    {
        var dialog = ProjectNameDialog.CreateForNew(
            "New Project",
            "Create",
            ProjectLibrary.Location
        );
        dialog.OnChosen += (location, name) =>
        {
            try
            {
                var folder = ProjectFolder.Create(location, name);
                ProjectLibrary.Location = location;
                ProjectLibrary.PutFirst(folder);
                Refresh(folder);
            }
            catch (Exception e) when (ProjectFolder.IsFileError(e))
            {
                UI.ShowMessage(
                    this,
                    "Can't Create Project",
                    $"Lizzie couldn't make a folder for \"{name}\".\n\n{e.Message}"
                );
            }
        };
        AddChild(dialog);
        dialog.Open();
    }

    private void OpenProject()
    {
        if (Selected is { } selected && !_open.Disabled)
            OpenRequested?.Invoke(selected.Folder);
    }

    private void RenameProject()
    {
        if (Selected is not { Found: true } selected)
            return;
        var dialog = ProjectNameDialog.CreateForRename(selected.Folder);
        dialog.OnChosen += (_, name) =>
        {
            try
            {
                var renamed = ProjectFolder.Rename(selected.Folder, name);
                ProjectLibrary.Renamed(selected.Folder, renamed);
                ProjectService.Instance?.FolderRenamed(selected.Folder, renamed);
                Refresh(renamed);
            }
            catch (Exception e) when (ProjectFolder.IsFileError(e))
            {
                UI.ShowMessage(
                    this,
                    "Can't Rename Project",
                    $"Lizzie couldn't rename \"{selected.Name}\". "
                        + $"Close anything using its folder and try again.\n\n{e.Message}"
                );
            }
        };
        AddChild(dialog);
        dialog.Open();
    }

    private void DeleteProject()
    {
        if (Selected is not { } selected)
            return;
        if (!selected.Found)
        {
            ProjectLibrary.Remove(selected.Folder);
            Refresh();
            return;
        }

        bool isOpen = selected.Folder == OpenFolder;
        var dialog = new ConfirmationDialog
        {
            Title = "Delete Project",
            DialogText = $"Delete \"{selected.Name}\"?\n\n",
            DialogAutowrap = true,
            OkButtonText = "Delete",
        };
        dialog.GetLabel().CustomMinimumSize = new Vector2(380, 0);
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            var error = ProjectLibrary.Trash(selected.Folder);
            if (error != Error.Ok)
            {
                GD.PrintErr($"Couldn't move '{selected.Folder}' to the trash: {error}");
                UI.ShowMessage(
                    this,
                    "Can't Delete Project",
                    $"Lizzie couldn't move \"{selected.Name}\" to the trash. "
                        + "Something may be using its files."
                );
                return;
            }
            if (isOpen)
                ProjectService.Instance?.MakeUntitled();
            Refresh();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
        // Deleting takes a deliberate click.
        dialog.GetCancelButton().GrabFocus();
    }

    private void AddExistingProject() =>
        ProjectNameDialog.PickFolder(
            this,
            "Add Existing Project",
            ProjectLibrary.Location,
            folder =>
            {
                if (!ProjectFolder.IsProject(folder))
                {
                    var save = SaveUpgrade.Current.FileName(ProjectFolder.SaveName);
                    UI.ShowMessage(
                        this,
                        "Not a Project",
                        $"\"{ProjectFolder.NameOf(folder)}\" doesn't have a Lizzie project in it. "
                            + $"A project's folder is empty, or has files like \"{save}\" in it."
                    );
                    return;
                }
                if (ProjectLibrary.NestedWith(folder) is { } other)
                {
                    var (inner, outer) = ProjectFolder.IsInside(folder, other)
                        ? (folder, other)
                        : (other, folder);
                    UI.ShowMessage(
                        this,
                        "Can't Add Project",
                        $"\"{ProjectFolder.NameOf(inner)}\" is inside \"{ProjectFolder.NameOf(outer)}\". "
                            + "A project cannot be saved inside another project."
                    );
                    return;
                }
                ProjectLibrary.PutFirst(folder);
                Refresh(folder);
            }
        );

    private void ShowInFolder()
    {
        if (Selected is { Found: true } selected)
            OS.ShellShowInFileManager(selected.Folder, openFolder: false);
    }
}
