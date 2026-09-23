using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

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

    public override void _Ready()
    {
        _instance = this;
        GD.Print("ProjectService initialized");

        SubscribeWatchers();

        Callable.From(SubscribeToEventLog).CallDeferred();
    }

    private void SubscribeToEventLog()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += OnEventApplied;
        AttachReplicated();
        UpdateWindowTitle();
    }

    /// <summary>
    /// Only used to track unsaved changes.
    /// </summary>
    private void OnEventApplied(TableEvent _)
    {
        if (EventSynchronizer.Instance?.BulkLoading == true)
            return;
        if (CurrentProject == null)
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

    /// <summary>
    /// The current project's templates.
    /// </summary>
    public ReplicatedDictionary<Template> Templates { get; } = new();

    /// <summary>
    /// The current project's prototypes.
    /// </summary>
    public ReplicatedDictionary<Prototype> Prototypes { get; } = new();

    /// <summary>
    /// The current project's datasets.
    /// </summary>
    public ReplicatedDictionary<DataSet> DataSets { get; } = new();

    /// <summary>
    /// The current project's dataset rows.
    /// </summary>
    public ReplicatedDictionary<DataRow> DataRows { get; } = new();

    /// <summary>
    /// The current project's images.
    /// </summary>
    public ReplicatedDictionary<Asset> Assets { get; } = new();

    /// <summary>
    /// The current project's saved snapshots.
    /// </summary>
    public ReplicatedDictionary<GameState> GameStates { get; } = new();

    /// <summary>
    /// The current project's settings, edited via the Project Settings dialog.
    /// </summary>
    public ReplicatedValue<ProjectGameSettings> Settings { get; } = new(() => new());

    /// <summary>
    /// The snapshot currently loaded or <see cref="SnowTag.Empty"/>.
    /// </summary>
    public ReplicatedValue<ActiveGameStateRef> ActiveGameState { get; } =
        new(() => new(), v => v.Id != SnowTag.Empty);

    /// <summary>Every replicated container, in compacted-save order. The single registry that
    /// drives attach, clear, bulk-load flush, and save.</summary>
    private IReadOnlyList<IReplicatedContainer> Containers =>
        [Settings, Templates, DataSets, DataRows, Prototypes, Assets, GameStates, ActiveGameState];

    private Project _currentProject;

    public Project CurrentProject
    {
        get => _currentProject;
        set
        {
            var replaced = !ReferenceEquals(_currentProject, value);
            _currentProject = value;
            AttachReplicated();

            if (replaced)
            {
                TextureCache.Instance.Clear();
                foreach (var c in Containers)
                    c.Clear();
            }

            UpdateWindowTitle();
            EventBus.Instance.Publish<ProjectChangedEvent>(); //no params means everything has changed
        }
    }

    /// <summary>
    /// Starts merging events into the replicated stores. Safe to call repeatedly.
    /// </summary>
    private void AttachReplicated()
    {
        if (EventSynchronizer.Instance == null)
            return;
        foreach (var c in Containers)
            c.Attach(EventSynchronizer.Instance);
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

    public void SettleAfterIngest()
    {
        GameObjects?.RebuildFromLog();
        SeedTagsFromLog();
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.BulkLoading = false;
        foreach (var c in Containers)
            c.FlushBulkLoad();

        HasUnsavedChanges = false;
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

        using (var saveFile = FileAccess.Open(tempVirtual, FileAccess.ModeFlags.Write))
        {
            if (saveFile == null)
            {
                GD.PrintErr(
                    $"Could not open temp save file '{tempVirtual}': {FileAccess.GetOpenError()}"
                );
                return false;
            }

            foreach (var e in BuildCompactedEvents())
                saveFile.StoreLine(JsonSerializer.Serialize(e, LizzieJson.EventOptions));
        }

        var tempReal = ProjectSettings.GlobalizePath(tempVirtual);
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
    private IEnumerable<TableEvent> BuildCompactedEvents()
    {
        var effects = new List<Effect>();

        foreach (var c in Containers)
            effects.AddRange(c.EnumerateSaveEffects());

        effects.AddRange(GameObjects?.GenerateCatchupEffects() ?? Array.Empty<Effect>());

        return [TableEvent.Now(null, effects.ToArray())];
    }

    /// <summary>
    /// Advances the tag counter past every SnowTag in the log.
    /// </summary>
    private static void SeedTagsFromLog()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        if (log == null)
            return;

        foreach (var e in log.Values)
            SnowTagWalker.Visit(e, Snowport.Clock.ObserveTag);
    }

    /// <summary>
    /// Creates or updates any replicated definition.
    /// </summary>
    public void Upsert<T>(T entity)
        where T : class, IReplicated
    {
        if (CurrentProject == null || entity == null)
            return;

        new UpsertBatch().Add(entity).Submit();
    }

    /// <summary>The non-deleted rows of a dataset in order.</summary>
    public List<DataRow> GetRows(SnowTag datasetRef) => ((IRecordReader)this).GetRows(datasetRef);

    public void UpdateGameSettings(ProjectGameSettings settings)
    {
        if (CurrentProject == null || settings == null)
            return;
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new SetReplicatedValueEffect<ProjectGameSettings> { Payload = settings }
            )
        );
    }

    /// <summary>
    /// Assigns hand and cursor container SnowTags to any seat that lacks one.
    /// </summary>
    public void EnsureSeatContainers()
    {
        if (CurrentProject == null)
            return;
        if (MultiplayerManager.Instance?.HasAuthority() == false)
            return;

        var settings = Settings.Value;
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
        }

        if (!changed)
            return;

        UpdateGameSettings(settings with { Players = builder.ToImmutable() });
    }

    /// <summary>
    /// Captures the current table as a new <see cref="GameState"/>.
    /// </summary>
    public void SaveGameState(string name, string description = "", bool link = false)
    {
        if (CurrentProject == null)
            return;

        var parent = link ? ActiveGameState.Value.Id : SnowTag.Empty;
        var state = new GameState
        {
            Id = Snowport.Clock.CreateTag(),
            Parent = parent,
            Name = name,
            Description = description,
            Upserts = BuildDelta(parent),
        };

        new UpsertBatch()
            .Add(state)
            .With(
                new SetReplicatedValueEffect<ActiveGameStateRef>
                {
                    Payload = new() { Id = state.Id },
                }
            )
            .Submit();
    }

    /// <summary>
    /// Saves over an existing snapshot in place.
    /// </summary>
    public void UpdateGameState(SnowTag stateRef)
    {
        if (CurrentProject == null || stateRef == SnowTag.Empty)
            return;
        if (!GameStates.Records.TryGetValue(stateRef, out var state) || state.Deleted)
            return;

        Upsert(state with { Upserts = BuildDelta(state.Parent) });
    }

    /// <summary>
    /// The current table expressed as a delta vs the given parent.
    /// </summary>
    private ImmutableArray<ComponentEffect> BuildDelta(SnowTag parent)
    {
        var parentFold = FoldChain(parent);
        var current = (GameObjects?.GenerateCatchupEffects() ?? Array.Empty<Effect>())
            .OfType<ComponentEffect>()
            .ToDictionary(e => e.Id);

        var delta = new List<ComponentEffect>();

        // added or transformed components
        foreach (var (id, ce) in current)
            if (!parentFold.TryGetValue(id, out var prev) || !StateEquals(prev.State, ce.State))
                delta.Add(ce);

        // removed components
        foreach (var (id, prev) in parentFold)
            if (!current.ContainsKey(id))
                delta.Add(
                    new ComponentEffect
                    {
                        Id = id,
                        PrototypeRef = prev.PrototypeRef,
                        State = new VcSyncDto
                        {
                            Location = VisualComponentBase.ComponentLocation.Deleted,
                        },
                    }
                );

        return delta.ToImmutableArray();
    }

    public void DeleteGameState(SnowTag stateRef)
    {
        if (CurrentProject == null)
            return;
        if (!GameStates.Records.TryGetValue(stateRef, out var state))
            return;

        // reject deleting a parent snapshot
        if (GameStates.Records.Values.Any(g => !g.Deleted && g.Parent == stateRef))
        {
            GD.PrintErr($"Cannot delete GameState '{state.Name}': it has child snapshots.");
            return;
        }

        Upsert(state with { Deleted = true });
    }

    /// <summary>
    /// Switches every client to a saved game state.
    /// </summary>
    public void SwitchGameState(SnowTag stateRef)
    {
        if (CurrentProject == null || GetGameState(stateRef) == null)
            return;

        var effects = new List<Effect>
        {
            new TableClearEffect(),
            new SetReplicatedValueEffect<ActiveGameStateRef> { Payload = new() { Id = stateRef } },
        };
        foreach (var ce in FoldChain(stateRef).Values)
        {
            var s = ce.State ?? new VcSyncDto();
            // Keep the captured transform and ZOrder intact so stacking is reproduced exactly.
            // Clear LastMoveId so ApplyUpsert falls back to this event's id and wins LWW.
            effects.Add(
                new ComponentEffect
                {
                    Id = ce.Id,
                    PrototypeRef = ce.PrototypeRef,
                    State = new VcSyncDto
                    {
                        Position = s.Position,
                        Rotation = s.Rotation,
                        DataSetRowIndex = s.DataSetRowIndex,
                        DataSetRowId = s.DataSetRowId,
                        Location = s.Location,
                        ContainerRef = s.ContainerRef,
                        ZOrder = s.ZOrder,
                        LastMoveId = SnowportId.Empty,
                    },
                }
            );
        }

        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(new GameStateSwitchAction { Target = stateRef }, effects.ToArray())
        );
    }

    /// <summary>
    /// Collect a snapshot's ancestory into a set of component upserts.
    /// </summary>
    private Dictionary<SnowTag, ComponentEffect> FoldChain(SnowTag stateRef)
    {
        var chain = new List<GameState>();
        var cursor = stateRef;
        while (cursor != SnowTag.Empty && GameStates.Records.TryGetValue(cursor, out var gs))
        {
            chain.Add(gs);
            cursor = gs.Parent;
        }
        chain.Reverse(); // root first

        var fold = new Dictionary<SnowTag, ComponentEffect>();
        foreach (var gs in chain)
        foreach (var up in gs.Upserts)
        {
            if (up.State?.Location == VisualComponentBase.ComponentLocation.Deleted)
                fold.Remove(up.Id);
            else
                fold[up.Id] = up;
        }
        return fold;
    }

    /// <summary>Compares two component states, ignoring the transient last-move id.</summary>
    private static bool StateEquals(VcSyncDto a, VcSyncDto b)
    {
        if (a == null || b == null)
            return a == b;
        return a.Position == b.Position
            && a.Rotation == b.Rotation
            && a.Location == b.Location
            && a.ContainerRef == b.ContainerRef
            && a.DataSetRowIndex == b.DataSetRowIndex
            && a.DataSetRowId == b.DataSetRowId
            && a.ZOrder.Target == b.ZOrder.Target
            && a.ZOrder.Suborder == b.ZOrder.Suborder
            && a.ZOrder.LastEvent == b.ZOrder.LastEvent;
    }

    public void AddPrototypeToManifest(CreateObjectEventArgs args)
    {
        if (CurrentProject == null)
            return;

        if (!Prototypes.Records.ContainsKey(args.PrototypeRef))
        {
            var name = !string.IsNullOrEmpty(args.Params?.ComponentName)
                ? args.Params.ComponentName
                : $"Unnamed {args.ComponentType}";

            Upsert(
                new Prototype
                {
                    Id = args.PrototypeRef,
                    Parameters = args.Params,
                    Name = name,
                }
            );
        }
    }

    /// <summary>
    /// The non-deleted snapshot or null.
    /// </summary>
    public GameState GetGameState(SnowTag stateRef)
    {
        if (stateRef == SnowTag.Empty)
            return null;
        if (GameStates.Records.TryGetValue(stateRef, out var state) && !state.Deleted)
            return state;
        return null;
    }

    public DataSet GetDataSet(SnowTag datasetRef)
    {
        if (datasetRef == SnowTag.Empty || CurrentProject == null)
            return null;
        if (DataSets.Records.TryGetValue(datasetRef, out var d) && !d.Deleted)
            return d;
        return null;
    }

    public Template GetTemplate(SnowTag templateRef)
    {
        if (templateRef == SnowTag.Empty || CurrentProject == null)
            return null;
        if (Templates.Records.TryGetValue(templateRef, out var t) && !t.Deleted)
            return t;
        return null;
    }

    private readonly Dictionary<SnowTag, Task> _inFlightFetches = new();

    public async Task<Image> FetchImageAsync(Asset asset)
    {
        if (asset == null)
            return null;

        if (AssetImageCache.Instance.IsDownloaded(asset))
            return AssetImageCache.Instance.GetImage(asset);

        Task fetch;
        lock (_inFlightFetches)
        {
            if (!_inFlightFetches.TryGetValue(asset.Id, out fetch))
            {
                fetch = DownloadAssetAsync(asset);
                _inFlightFetches[asset.Id] = fetch;
            }
        }

        try
        {
            await fetch;
        }
        finally
        {
            lock (_inFlightFetches)
            {
                if (
                    _inFlightFetches.TryGetValue(asset.Id, out var current)
                    && current == fetch
                    && fetch.IsCompleted
                )
                {
                    _inFlightFetches.Remove(asset.Id);
                }
            }
        }

        return AssetImageCache.Instance.GetImage(asset);
    }

    private static async Task DownloadAssetAsync(Asset asset)
    {
        try
        {
            var service = new CloudAssetService();
            await service.InitializeAsync(
                CloudProviderType.GoogleDrive,
                string.Empty,
                string.Empty
            );

            var r = await service.DownloadImageAsync(asset.CloudPath);

            if (!string.IsNullOrWhiteSpace(r.Item1))
            {
                GD.PrintErr(r.Item1);
                return;
            }

            AssetImageCache.Instance.Store(asset, r.Item2);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Image fetch failed: {ex.Message}");
        }
    }

    public GameObjects GameObjects { get; set; }

    public VisualComponentBase SpawnDisconnectedVisualComponent(
        Prototype prototype,
        TextureFactory textureFactory
    )
    {
        var scenePath = Utility.ComponentTypeToScenePath(prototype.Type, prototype.Parameters);

        VisualComponentBase component = SpawnComponent(scenePath);

        if (component == null)
        {
            GD.PrintErr("Null Spawn Component");
            return null;
        }

        component.NeverHighlight = true;

        component.PrototypeRef = prototype.Id;

        if (component.Setup(prototype.Id, textureFactory))
        {
            return component;
        }
        else
        {
            GD.PrintErr("Error building component");
            return null;
        }
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

    public CommandDictionary CommandDictionary => new CommandDictionary();
    public float RotationStep { get; set; } = 15;
}
