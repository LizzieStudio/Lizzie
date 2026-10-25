using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
        var filename = string.IsNullOrWhiteSpace(CurrentProject?.Filename)
            ? "Untitled"
            : CurrentProject.Filename;
        var marker = HasUnsavedChanges ? "*" : "";
        GetWindow().Title = $"{filename}{marker}: {AppName}";
    }

    /// <summary>
    /// Starts a fresh, unnamed game.
    /// </summary>
    public void NewGame()
    {
        CurrentProject = new Project();
        HasUnsavedChanges = false;
        EnsureSeatContainers();
        EnsureTable();
        PresenceSynchronizer.Instance?.SeatSoloPlayer();
    }

    /// <summary>
    /// Replaces everything with a fresh, unnamed game, like before joining a game.
    /// </summary>
    public void ReplaceWithNewGame()
    {
        GameObjects?.ResetForLoad();
        NewGame();
    }

    /// <summary>
    /// Gets the save file name with version number.
    /// </summary>
    private static string SaveFile(string name, int format) => $"{name}.v{format}.proj";

    /// <summary>
    /// Loads the highest version save that this version of Lizzie can read.
    /// Returns null when the project has no save.
    /// </summary>
    /// <param name="name">The name of the file to attempt to load</param>
    /// <param name="warning">A warning to give the player when the load succeeds with an important issue.</param>
    /// <exception cref="ProjectLoadException" />
    public Project LoadProject(string name, out string warning)
    {
        var formats = DirAccess
            .GetFilesAt("user://")
            .SelectMany<string, int?>(file =>
            {
                if (Regex.Match(file, @"^(.+)\.v(\d+)\.proj$") is { Success: true } match)
                {
                    var filename = match.Groups[1].Value;
                    var success = int.TryParse(match.Groups[2].Value, out var version);
                    return filename == name && success ? [version] : [];
                }
                else if (Regex.Match(file, @"^(.+)\.proj$") is { Success: true } original)
                {
                    var filename = original.Groups[1].Value;
                    return filename == name ? [0] : [];
                }
                return [];
            })
            .ToList();
        int? newer = formats.Where(f => f > SaveUpgrade.Current).Max();
        int? readable = formats.Where(f => f <= SaveUpgrade.Current).Max();
        var file = readable is int format and > 0 ? SaveFile(name, format) : $"{name}.proj";
        var path = $"user://{file}";
        var newerFile = newer is int n ? SaveFile(name, n) : null;
        var currentFile = SaveFile(name, SaveUpgrade.Current);

        warning = null;
        if (!FileAccess.FileExists(path))
        {
            if (newerFile != null)
                throw new ProjectLoadException(
                    $"\"{newerFile}\" was saved by a newer version of Lizzie. Update Lizzie to open it."
                );
            return null;
        }

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

            if (SaveUpgrade.Run(lines) < SaveUpgrade.Current)
                warnings.Add(
                    $"\"{file}\" was saved by an older version of Lizzie, so it was migrated. "
                        + "Some features may have changed. "
                        + $"Saving will not overwrite \"{file}\". "
                        + $"The new file will be \"{currentFile}\" instead."
                );
            events = lines
                .Select(l => l.Deserialize<TimelineEvent>(LizzieJson.EventOptions))
                .ToArray();
        }
        catch (Exception ex)
        {
            throw new ProjectLoadException(
                $"\"{file}\" couldn't be read. It may be damaged.\n\n{ex.Message}"
            );
        }

        GameObjects?.ResetForLoad();

        var project = new Project { Filename = name };
        CurrentProject = project;

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.BulkLoading = true;
        foreach (var e in events)
            EventSynchronizer.Instance?.Ingest(e);

        SettleAfterIngest();
        warning = warnings.Count > 0 ? string.Join("\n\n", warnings) : null;
        return project;
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
        EnsureSeatContainers();
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
    /// <item>cancels their open gestures</item>
    /// <item>deletes their selection</item>
    /// <item>frees their seat</item>
    /// </list>
    /// </summary>
    public void AbandonPlayer(byte source)
    {
        EventSynchronizer.Instance?.AbandonGroups(source);
        var seating = Atoms.R.Get(Seating.Only);
        Atoms.R.WriteAdmin(
            Atoms
                .R.Get(Selection.ByPlayer, source)
                .Select(Atoms.R.Unset)
                .Append<IChange>(seating with { Seats = seating.Seats.Remove(source) })
        );
    }

    public bool SaveProject(Project project)
    {
        // an unnamed project will not autosave
        // this happens after joining a game
        if (string.IsNullOrWhiteSpace(project.Filename))
            return false;

        var sync = EventSynchronizer.Instance;
        if (sync == null)
            return false;

        var finalVirtual = $"user://{SaveFile(project.Filename, SaveUpgrade.Current)}";
        var tempVirtual = $"user://{project.Filename}.{OS.GetProcessId()}.proj.tmp";

        var tempReal = ProjectSettings.GlobalizePath(tempVirtual);
        using (var saveFile = FileAccess.Open(tempVirtual, FileAccess.ModeFlags.Write))
        {
            if (saveFile == null)
            {
                GD.PrintErr(
                    $"Could not open temp save file '{tempVirtual}': {FileAccess.GetOpenError()}"
                );
                return false;
            }

            try
            {
                saveFile.StoreLine(SaveUpgrade.Header);
                saveFile.StoreLine(
                    SaveCompaction.Serialize(
                        TimelineEvent.Admin(
                            SaveCompaction.Collect(Atoms.R.Machinery().SavedAtoms())
                        )
                    )
                );
            }
            catch (Exception ex)
            {
                // the old save is left as it was
                GD.PrintErr($"Could not save '{finalVirtual}': {ex.Message}");
                saveFile.Close();
                System.IO.File.Delete(tempReal);
                return false;
            }
        }

        try
        {
            System.IO.File.Move(tempReal, ProjectSettings.GlobalizePath(finalVirtual), true);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Could not finalize save '{finalVirtual}': {ex.Message}");
            try
            {
                System.IO.File.Delete(tempReal);
            }
            catch { }
            return false;
        }

        HasUnsavedChanges = false;
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
            JsonWalker.Visit<SnowTag>(e, Snowport.Clock.ObserveTag);
    }

    /// <summary>
    /// Assigns hand, cursor, and over-the-hand container SnowTags to any seat that lacks them.
    /// The host or a solo player runs it after a new game or a load.
    /// </summary>
    private void EnsureSeatContainers()
    {
        if (CurrentProject == null)
            return;
        if (MultiplayerManager.Instance?.HasAuthority() == false)
            return;

        var settings = Atoms.R.Get(ProjectGameSettings.Only);
        var builder = settings.Players.ToBuilder();
        bool changed = false;
        for (int i = 0; i < builder.Count; i++)
        {
            if (builder[i].HandRef == SnowTag.Empty)
            {
                builder[i] = builder[i] with { HandRef = SnowTag.Create() };
                changed = true;
            }
            if (builder[i].CursorRef == SnowTag.Empty)
            {
                builder[i] = builder[i] with { CursorRef = SnowTag.Create() };
                changed = true;
            }
            if (builder[i].HandHoldRef == SnowTag.Empty)
            {
                builder[i] = builder[i] with { HandHoldRef = SnowTag.Create() };
                changed = true;
            }
        }

        if (!changed)
            return;

        Atoms.R.WriteAdmin(settings with { Players = builder.ToImmutable() });
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
        var table = Stage.NewTable(Atoms.R, SnowTag.Empty);
        Atoms.R.WriteAdmin([table, Atoms.R.Get(CurrentTable.Only) with { StageRef = table.Id }]);
    }

    public GameObjects GameObjects { get; set; }

    public VisualComponentBase SpawnDisconnectedVisualComponent(
        Prototype prototype,
        PrototypeItemRef item,
        TextureFactory textureFactory
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
        component.TextureFactory = textureFactory;

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
