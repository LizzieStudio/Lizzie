using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;
using Lizzie.Replication.Machinery;

public partial class ProjectService : Node
{
    public const string SampleProjectName = "Test Project";

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

        Callable.From(SubscribeToEventLog).CallDeferred();
    }

    private void SubscribeToEventLog()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += OnEventApplied;
        UpdateWindowTitle();
    }

    /// <summary>
    /// Tracks unsaved changes.
    /// </summary>
    private void OnEventApplied(TableEvent e)
    {
        if (EventSynchronizer.Instance?.BulkLoading == true || CurrentProject == null)
            return;
        HasUnsavedChanges = true;
    }

    // What the project's records are read through.
    private static IRecordReader R => RecordService.Instance;

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
                TextureCache.Instance.Clear();
                RecordService.Instance.Machinery().Clear();
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
    public void NewGame(string filename = null)
    {
        CurrentProject = new Project { Filename = filename };
        HasUnsavedChanges = false;
        EnsureSingletons();
        EnsureSeatContainers();
        PresenceSynchronizer.Instance?.SeatSoloPlayer();
    }

    /// <summary>
    /// Replaces the table with a fresh, unnamed game, like before joining a game.
    /// </summary>
    public void ReplaceWithNewGame()
    {
        GameObjects?.ResetForLoad();
        NewGame();
    }

    /// <summary>
    /// Reflects to get every type marked <see cref="SingletonAttribute"/> which table setup needs to create.
    /// </summary>
    private static readonly Type[] SingletonTypes = typeof(ProjectService)
        .Assembly.GetTypes()
        .Where(t => t.IsDefined(typeof(SingletonAttribute), false))
        .ToArray();

    /// <summary>
    /// Creates each <see cref="SingletonAttribute"/> record the table is missing in one admin event.
    /// The host or a solo player runs it after a new game or a load.
    /// </summary>
    public void EnsureSingletons()
    {
        if (MultiplayerManager.Instance?.HasAuthority() == false)
            return;

        var create = typeof(ProjectService).GetMethod(
            nameof(CreateIfMissing),
            BindingFlags.NonPublic | BindingFlags.Static
        );
        RecordService.Instance.WriteAdmin(
            SingletonTypes
                .Select(type => (Replicated)create.MakeGenericMethod(type).Invoke(null, null))
                .Where(record => record != null)
        );
    }

    /// <summary>
    /// A new <typeparamref name="T"/> with its defaults, or null if the table has one.
    /// </summary>
    private static T CreateIfMissing<T>()
        where T : Replicated
    {
        if (R.Get<T>().Count > 0)
            return null;

        Replicated record = Activator.CreateInstance<T>();
        return (T)(record with { Id = Snowport.Clock.CreateTag() });
    }

    public Project LoadProject(string name)
    {
        if (!FileAccess.FileExists($"user://{name}.proj"))
        {
            return null; // Error! We don't have a save to load.
        }

        GameObjects?.ResetForLoad();

        var project = new Project { Filename = name };
        CurrentProject = project;

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.BulkLoading = true;

        // the file is a list of events using JSON-lines
        using var loadFile = FileAccess.Open($"user://{name}.proj", FileAccess.ModeFlags.Read);
        while (!loadFile.EofReached())
        {
            var line = loadFile.GetLine();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            TableEvent e;
            try
            {
                e = JsonSerializer.Deserialize<TableEvent>(line, LizzieJson.EventOptions);
            }
            catch (Exception ex)
            {
                // a broken line is skipped
                GD.PrintErr($"Skipping unreadable project line: {ex.Message}");
                continue;
            }

            EventSynchronizer.Instance?.Ingest(e);
        }
        loadFile.Close();

        SettleAfterIngest();
        return project;
    }

    /// <summary>
    /// Replaces the table with the host's, from the events collected while joining,
    /// all in one frame, so nothing sees the table half loaded.
    /// </summary>
    public void LoadJoinedTable(IEnumerable<TableEvent> events)
    {
        GameObjects?.ResetTable();
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
        SeedTagsFromLog();
        EndBulkLoad();
        EnsureSingletons();
        EnsureSeatContainers();
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
        RecordService.Instance.Machinery().FlushBulkLoad();
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
        var seating = R.Single<Seating>();
        RecordService.Instance.WriteAdmin(
            R.Get<Selection>(s => s.Player == source)
                .Select(s => s with { Deleted = true })
                .Append<Replicated>(seating with { Seats = seating.Seats.Remove(source) })
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

        var finalVirtual = $"user://{project.Filename}.proj";
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
                saveFile.StoreLine(SaveCompaction.Serialize(BuildCompactedEvent()));
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
    /// Rebuilds the event log as one event holding the current state.
    /// </summary>
    private static TableEvent BuildCompactedEvent() =>
        TableEvent.Admin(
            SaveCompaction
                .Collect(RecordService.Instance.Machinery().SavedRecords())
                // sorted for determinism
                .OrderBy(r => r.Id)
                .ThenBy(r => r.GetType().Name, StringComparer.Ordinal)
                .ToArray()
        );

    /// <summary>
    /// Advances the tag counter past every SnowTag in the log.
    /// </summary>
    private static void SeedTagsFromLog()
    {
        var log = EventSynchronizer.Instance?.Log;
        if (log == null)
            return;

        foreach (var e in log.Values)
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

        var settings = R.Single<ProjectGameSettings>();
        var builder = settings.Players.ToBuilder();
        bool changed = false;
        for (int i = 0; i < builder.Count; i++)
        {
            if (builder[i].HandRef == SnowTag.Empty)
            {
                builder[i] = builder[i] with { HandRef = Snowport.Clock.CreateTag() };
                changed = true;
            }
            if (builder[i].CursorRef == SnowTag.Empty)
            {
                builder[i] = builder[i] with { CursorRef = Snowport.Clock.CreateTag() };
                changed = true;
            }
            if (builder[i].HandHoldRef == SnowTag.Empty)
            {
                builder[i] = builder[i] with { HandHoldRef = Snowport.Clock.CreateTag() };
                changed = true;
            }
        }

        if (!changed)
            return;

        RecordService.Instance.WriteAdmin(settings with { Players = builder.ToImmutable() });
    }

    public GameObjects GameObjects { get; set; }

    public VisualComponentBase SpawnDisconnectedVisualComponent(
        Prototype prototype,
        TextureFactory textureFactory
    )
    {
        var scenePath = Utility.ComponentTypeToScenePath(
            prototype.Type,
            prototype.Parameters,
            RecordService.Instance
        );

        VisualComponentBase component = SpawnComponent(scenePath);

        if (component == null)
        {
            GD.PrintErr("Null Spawn Component");
            return null;
        }

        component.NeverHighlight = true;
        component.PrototypeRef = prototype.Id;
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
