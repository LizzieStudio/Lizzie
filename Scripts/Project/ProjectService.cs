using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Lizzie.Replication.Machinery;
using SaveUpgrades;

namespace Lizzie;

public partial class ProjectService : Node
{
    private static ProjectService _instance;

    public static ProjectService Instance
    {
        get
        {
            if (_instance == null)
            {
                GD.PrintErr(
                    "ProjectService instance not initialized. Make sure ProjectService is added as an AutoLoad."
                );
            }
            return _instance;
        }
    }

    private const string AppName = "Lizzie";

    public override void _EnterTree()
    {
        _instance = this;
    }

    public override void _Ready()
    {
        GD.Print("ProjectService initialized");

        Callable.From(SubscribeToTimeline).CallDeferred();
    }

    private void SubscribeToTimeline()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += OnEventApplied;
        UpdateWindowTitle();
    }

    /// <summary>
    /// Tracks unsaved changes.
    /// </summary>
    private void OnEventApplied(TimelineEvent e)
    {
        if (EventSynchronizer.Instance?.BulkLoading == true || CurrentProject == null)
            return;
        HasUnsavedChanges = true;
    }

    private bool _hasUnsavedChanges;

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value)
                return;
            _hasUnsavedChanges = value;
            UpdateWindowTitle();
        }
    }

    private Project _currentProject;

    /// <summary>
    /// Invoked after another project replaces the open one, like by opening or joining one.
    /// </summary>
    public event Action ProjectReplaced;

    public Project CurrentProject
    {
        get => _currentProject;
        set
        {
            var replaced = !ReferenceEquals(_currentProject, value);
            _currentProject = value;

            if (replaced)
            {
                FaceSet.Clear();
                Atoms.R.Machinery().Clear();
                ProjectReplaced?.Invoke();
            }

            UpdateWindowTitle();
        }
    }

    /// <summary>
    /// Shows the project name and a * if there are unsaved changes.
    /// </summary>
    private void UpdateWindowTitle()
    {
        if (!IsInsideTree())
            return;
        var name = CurrentProject?.Name ?? "Untitled";
        var marker = HasUnsavedChanges ? "*" : "";
        GetWindow().Title = $"{name}{marker}: {AppName}";
    }

    /// <summary>
    /// Starts a fresh, empty game.
    /// </summary>
    /// <param name="folder">The project folder it saves to, or null for an Untitled game.</param>
    public void NewGame(string folder = null)
    {
        CurrentProject = new Project { Folder = folder };
        Atoms.R.WriteAdmin(Seat.Defaults());
        EnsureTable();
        PresenceSynchronizer.Instance?.SeatSoloPlayer();
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Replaces everything with a fresh, empty game, like before joining a game.
    /// </summary>
    /// <param name="folder">The project folder it saves to, or null for an Untitled game.</param>
    public void ReplaceWithNewGame(string folder = null)
    {
        GameObjects?.ResetForLoad();
        NewGame(folder);
    }

    /// <summary>
    /// Opens the project in <paramref name="folder"/> or starts it empty when it has no saves yet.
    /// </summary>
    /// <param name="warning">A warning to give the player when it opens with an important issue.</param>
    /// <exception cref="ProjectLoadException" />
    public void OpenProject(string folder, out string warning)
    {
        if (!LoadProject(folder, out warning))
            ReplaceWithNewGame(folder);
        ProjectLibrary.Opened(folder);
    }

    /// <summary>
    /// Saves the project into <paramref name="folder"/> and makes that the save location from now on.
    /// False when it couldn't save, which keeps the old save location.
    /// </summary>
    public bool SaveProjectAs(string folder)
    {
        if (CurrentProject == null)
            return false;
        var was = CurrentProject.Folder;
        CurrentProject.Folder = folder;
        if (!SaveProject(CurrentProject))
        {
            CurrentProject.Folder = was;
            return false;
        }
        ProjectLibrary.Opened(folder);
        UpdateWindowTitle();
        return true;
    }

    /// <summary>
    /// Switches the project to "Untitled", which doesn't have a save location.
    /// </summary>
    public void MakeUntitled()
    {
        if (CurrentProject == null)
            return;
        CurrentProject.Folder = null;
        HasUnsavedChanges = true;
        UpdateWindowTitle();
    }

    public void FolderRenamed(string folder, string renamed)
    {
        if (CurrentProject?.Folder != folder)
            return;
        CurrentProject.Folder = renamed;
        UpdateWindowTitle();
    }

    /// <summary>
    /// Loads the highest version save in <paramref name="folder"/> that this version of Lizzie can read.
    /// False when the project has no save.
    /// </summary>
    /// <param name="folder">The project's folder.</param>
    /// <param name="warning">A warning to give the player when the load succeeds with an important issue.</param>
    /// <exception cref="ProjectLoadException" />
    private bool LoadProject(string folder, out string warning)
    {
        // The same reading of the folder the project manager shows.
        var status = ProjectStatus.Of(folder);
        if (!status.Found)
            throw new ProjectLoadException(
                $"The folder \"{folder}\" can't be found. It may have been moved, renamed or deleted."
            );
        var name = ProjectFolder.SaveName;
        var newerFile = status.Newer?.FileName(name);
        var currentFile = SaveUpgrade.Current.FileName(name);

        warning = null;
        if (status.Readable is not { } readable)
        {
            if (newerFile != null)
                throw new ProjectLoadException(
                    $"\"{newerFile}\" was saved by a newer version of Lizzie. Update Lizzie to open it."
                );
            return false;
        }
        var file = readable.FileName(name);
        var path = ProjectFolder.SavePath(folder, readable);

        var warnings = new List<string>();
        if (newerFile != null)
            warnings.Add(
                $"\"{newerFile}\" was saved by a newer version of Lizzie, so \"{file}\" was opened instead. "
                    + $"Upgrade Lizzie to open \"{newerFile}\"."
            );

        TimelineEvent[] events;
        try
        {
            // the file has a header and a list of events using JSON-lines
            var lines = new List<JsonObject>();
            using (var loadFile = FileAccess.Open(path, FileAccess.ModeFlags.Read))
                while (!loadFile.EofReached())
                {
                    var line = loadFile.GetLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        lines.Add(JsonNode.Parse(line).AsObject());
                }

            var checkpoints = new List<(SaveFormat Format, string[] Lines)>();
            bool upgraded = SaveUpgrade.Run(
                lines,
                format => checkpoints.Add((format, [.. lines.Select(l => l.ToJsonString())]))
            );
            if (upgraded)
            {
                warnings.Add(
                    $"\"{file}\" was saved by an older version of Lizzie, so it was migrated. "
                        + "Some features may have changed. "
                        + $"Saving will not overwrite \"{file}\". "
                        + (
                            checkpoints.Count > 0
                                ? $"The migrated save was written to \"{currentFile}\", which saving overwrites."
                                : $"The new file will be \"{currentFile}\" instead."
                        )
                );
            }
            events = lines
                .Select(l => l.Deserialize<TimelineEvent>(LizzieJson.EventOptions))
                .ToArray();

            foreach (var (format, saved) in checkpoints)
            {
                WriteSave(
                    ProjectFolder.SavePath(folder, format),
                    save =>
                    {
                        save.StoreLine(format.Header);
                        foreach (var line in saved)
                            save.StoreLine(line);
                    }
                );
            }
        }
        catch (Exception ex)
        {
            throw new ProjectLoadException(
                $"\"{file}\" couldn't be read. It may be damaged.\n\n{ex.Message}"
            );
        }

        GameObjects?.ResetForLoad();
        CurrentProject = new Project { Folder = folder };

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.BulkLoading = true;
        foreach (var e in events)
            EventSynchronizer.Instance?.Ingest(e);

        SettleAfterIngest();
        warning = warnings.Count > 0 ? string.Join("\n\n", warnings) : null;
        return true;
    }

    /// <summary>
    /// Replaces the timeline with the host's, from the events collected while joining,
    /// all in one frame, so nothing sees the project half loaded.
    /// </summary>
    public void LoadJoinedTimeline(IEnumerable<TimelineEvent> events)
    {
        GameObjects?.ResetView();
        EventSynchronizer.Instance?.Clear();
        CurrentProject = new Project();

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.BulkLoading = true;
        foreach (var e in events)
            EventSynchronizer.Instance?.Ingest(e);

        SettleAfterIngest();
    }

    private void SettleAfterIngest()
    {
        SeedTagsFromTimeline();
        EndBulkLoad();
        EnsureTable();
        PresenceSynchronizer.Instance?.SeatSoloPlayer();

        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Stops bulk loading and sends each store's one notification for everything it merged.
    /// </summary>
    public void EndBulkLoad()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.BulkLoading = false;
        Atoms.R.Machinery().FlushBulkLoad();
    }

    /// <summary>
    /// Cleans up after a player who is gone.
    /// <list type="bullet">
    /// <item>cancels their open transitions</item>
    /// <item>deletes their selection</item>
    /// <item>frees their seat</item>
    /// </list>
    /// </summary>
    public void AbandonPlayer(Source source)
    {
        EventSynchronizer.Instance?.AbandonTransitions(source);
        var seating = Atoms.R.Get(Seating.Only);
        Atoms.R.WriteAdmin(
            Atoms.R.UnsetAll(Selection.ByPlayer, source),
            seating with
            {
                Seats = seating.Seats.Remove(source),
            }
        );
    }

    /// <summary>
    /// Saves <paramref name="project"/> into its folder.
    /// </summary>
    /// <returns>
    /// False when it has no folder, or the save failed.
    /// </returns>
    public bool SaveProject(Project project)
    {
        // an Untitled project has nowhere to save, like after joining a game
        if (project.Folder == null)
            return false;

        var sync = EventSynchronizer.Instance;
        if (sync == null)
            return false;

        var saved = WriteSave(
            ProjectFolder.SavePath(project.Folder, SaveUpgrade.Current),
            save =>
            {
                save.StoreLine(SaveUpgrade.Current.Header);
                save.StoreLine(
                    SaveCompaction.Serialize(SaveCompaction.Event(Atoms.R.Machinery().SavedAtoms()))
                );
            }
        );
        if (saved)
            HasUnsavedChanges = false;
        return saved;
    }

    /// <summary>
    /// Writes the save file at <paramref name="path"/> through a temporary file, so a failed write leaves the old file as it was.
    /// </summary>
    private static bool WriteSave(string path, Action<FileAccess> write)
    {
        var temp = $"{path}.{OS.GetProcessId()}.tmp";

        using (var saveFile = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (saveFile == null)
            {
                GD.PrintErr($"Could not open temp save file '{temp}': {FileAccess.GetOpenError()}");
                return false;
            }

            try
            {
                write(saveFile);
            }
            catch (Exception ex)
            {
                // the old save is left as it was
                GD.PrintErr($"Could not save '{path}': {ex.Message}");
                saveFile.Close();
                System.IO.File.Delete(temp);
                return false;
            }
        }

        try
        {
            System.IO.File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Could not finalize save '{path}': {ex.Message}");
            try
            {
                System.IO.File.Delete(temp);
            }
            catch { }
            return false;
        }
        return true;
    }

    public bool SaveProject()
    {
        if (CurrentProject == null)
            return false;
        return SaveProject(CurrentProject);
    }

    /// <summary>
    /// Advances the tag counter past every SnowTag in the timeline.
    /// </summary>
    private static void SeedTagsFromTimeline()
    {
        var timeline = EventSynchronizer.Instance?.Timeline;
        if (timeline == null)
            return;

        foreach (var e in timeline.Values)
            JsonWalker.Visit<SnowTag>(e, SnowTag.Observe);
    }

    /// <summary>
    /// Starts an empty table at the root unless the current table is in the tree, which can't be undone.
    /// The current table is only missing from the tree in a new project,
    /// or when two players' changes crossed, like one deleting the blueprint another made a table current on.
    /// The host or a solo player runs it after a new game or a load.
    /// </summary>
    private void EnsureTable()
    {
        if (CurrentProject == null)
            return;
        if (MultiplayerManager.Instance?.HasAuthority() == false)
            return;
        if (Stage.Reachable(Atoms.R, Atoms.R.Get(CurrentTable.Only).StageRef))
            return;
        var table = Stage.NewTable(Atoms.R, Ref<Stage>.None);
        Atoms.R.WriteAdmin([table, Atoms.R.Get(CurrentTable.Only) with { StageRef = table.Id }]);
    }

    public GameObjects GameObjects { get; set; }

    public VisualComponentBase SpawnDisconnectedVisualComponent(
        Prototype prototype,
        PrototypeItemRef item
    )
    {
        var scenePath = Utility.ComponentTypeToScenePath(
            prototype.Type,
            prototype.Parameters,
            Atoms.R,
            item != null
        );

        VisualComponentBase component = SpawnComponent(scenePath);

        if (component == null)
        {
            GD.PrintErr("Null Spawn Component");
            return null;
        }

        component.NeverHighlight = true;
        component.PrototypeRef = prototype.Id;
        component.Item = item;

        return component;
    }

    public VisualComponentBase SpawnComponent(string prototypeScene)
    {
        var scene = ResourceLoader.Load<PackedScene>(prototypeScene).Instantiate();

        if (scene is VisualComponentBase vcb)
        {
            return vcb;
        }
        return null;
    }
}
