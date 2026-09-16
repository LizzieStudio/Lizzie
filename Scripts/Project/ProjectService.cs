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

    public override void _Ready()
    {
        _instance = this;
        GD.Print("ProjectService initialized");
    }

    private Project _currentProject;

    public Project CurrentProject
    {
        get => _currentProject;
        set
        {
            if (!ReferenceEquals(_currentProject, value))
                TextureCache.Instance.Clear();
            _currentProject = value;
            EventBus.Instance.Publish<ProjectChangedEvent>(); //no params means everything has changed
            EventBus.Instance.Publish<ProjectSettingsChangedEvent>();
        }
    }

    /// <summary>
    /// Starts a fresh, unnamed game.
    /// </summary>
    public void NewGame() => CurrentProject = new Project();

    public Project LoadProject(string name)
    {
        if (!FileAccess.FileExists($"user://{name}.proj"))
        {
            return null; // Error! We don't have a save to load.
        }

        GameObjects?.ResetForLoad();

        var project = new Project { Filename = name };
        CurrentProject = project;

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

        SeedTagsFromProject(project);
        return project;
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

        using var saveFile = FileAccess.Open(
            $"user://{project.Filename}.proj",
            FileAccess.ModeFlags.Write
        );

        var line = JsonSerializer.Serialize(
            TableEvent.Now(null, sync.BuildProjectEffects()),
            LizzieJson.EventOptions
        );
        saveFile.StoreLine(line);

        saveFile.Close();

        return true;
    }

    public bool SaveProject()
    {
        if (CurrentProject == null)
            return false;
        return SaveProject(CurrentProject);
    }

    /// <summary>
    /// Advances the tag counter past every SnowTag.
    /// </summary>
    private static void SeedTagsFromProject(Project project)
    {
        // I'd like to find a way to get rid of this method.
        // It's only necessary for the ZOrder.

        if (project == null)
            return;

        var clock = Snowport.Clock;

        void ObserveTags<T>(IEnumerable<T> items)
            where T : IReplicated
        {
            foreach (var it in items)
                clock.ObserveTag(it.Id);
        }

        ObserveTags(project.Templates.Values);
        ObserveTags(project.Datasets.Values);
        ObserveTags(project.Prototypes.Values);
        ObserveTags(project.Images.Values);
        ObserveTags(project.GameStates.Values);

        clock.ObserveTag(project.ActiveGameState);

        foreach (var gs in project.GameStates.Values)
        foreach (var up in gs.Upserts ?? Array.Empty<ComponentEffect>())
        {
            clock.ObserveTag(up.Id);
            clock.ObserveTag(up.PrototypeRef);
            if (up.State != null)
                clock.ObserveTag(up.State.ContainerRef);
        }

        var players = project.GameSettings?.Players ?? default;
        if (!players.IsDefaultOrEmpty)
            foreach (var p in players)
                clock.ObserveTag(p.HandRef);
    }

    public void UpdateDataSet(DataSet dataset)
    {
        if (CurrentProject == null || dataset == null)
            return;
        if (dataset.Id == SnowTag.Empty)
            dataset.Id = Snowport.Clock.CreateTag();
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<DataSet> { Id = dataset.Id, Payload = dataset }
            )
        );
    }

    public void UpdateTemplate(Template template)
    {
        if (CurrentProject == null || template == null)
            return;
        if (template.Id == SnowTag.Empty)
            template.Id = Snowport.Clock.CreateTag();
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<Template> { Id = template.Id, Payload = template }
            )
        );
    }

    public void UpdateGameSettings(ProjectGameSettings settings)
    {
        if (CurrentProject == null || settings == null)
            return;
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(null, new UpdateSettingsEffect { Payload = settings })
        );
    }

    /// <summary>
    /// Assigns a hand container SnowTag to any seat that lacks one.
    /// </summary>
    public void EnsureSeatHands()
    {
        if (CurrentProject == null)
            return;
        if (MultiplayerManager.Instance?.HasAuthority() == false)
            return;

        var settings = CurrentProject.GameSettings;
        var builder = settings.Players.ToBuilder();
        bool changed = false;
        for (int i = 0; i < builder.Count; i++)
        {
            if (builder[i].HandRef == SnowTag.Empty)
            {
                builder[i] = builder[i] with { HandRef = Snowport.Clock.CreateTag() };
                changed = true;
            }
        }

        if (!changed)
            return;

        UpdateGameSettings(settings with { Players = builder.ToImmutable() });
    }

    public void UpdatePrototype(Prototype prototype)
    {
        if (CurrentProject == null || prototype == null)
            return;
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<Prototype> { Id = prototype.Id, Payload = prototype }
            )
        );
    }

    public void DeletePrototype(SnowTag prototypeRef)
    {
        if (CurrentProject == null)
            return;
        if (!CurrentProject.Prototypes.TryGetValue(prototypeRef, out var prototype))
            return;
        prototype.Deleted = true;
        UpdatePrototype(prototype);
        SaveProject(CurrentProject);
    }

    public void UpdateImage(Asset image)
    {
        if (CurrentProject == null || image == null)
            return;
        if (image.Id == SnowTag.Empty)
            image.Id = Snowport.Clock.CreateTag();
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<Asset> { Id = image.Id, Payload = image }
            )
        );
    }

    /// <summary>
    /// Captures the current table as a new <see cref="GameState"/>.
    /// </summary>
    public GameState SaveGameState(string name, string description = "", bool link = false)
    {
        if (CurrentProject == null)
            return null;

        var parent = link ? CurrentProject.ActiveGameState : SnowTag.Empty;
        var state = new GameState
        {
            Id = Snowport.Clock.CreateTag(),
            Parent = parent,
            Name = name,
            Description = description,
            Upserts = BuildDelta(parent),
        };

        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<GameState> { Id = state.Id, Payload = state },
                new ActiveGameStateEffect { Target = state.Id }
            )
        );
        SaveProject(CurrentProject);
        return state;
    }

    /// <summary>
    /// Saves over an existing snapshot in place.
    /// </summary>
    public void UpdateGameState(SnowTag stateRef)
    {
        if (CurrentProject == null || stateRef == SnowTag.Empty)
            return;
        if (!CurrentProject.GameStates.TryGetValue(stateRef, out var state) || state.Deleted)
            return;

        state.Upserts = BuildDelta(state.Parent);

        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<GameState> { Id = state.Id, Payload = state }
            )
        );
        SaveProject(CurrentProject);
    }

    /// <summary>
    /// The current table expressed as a delta vs the given parent.
    /// </summary>
    private ComponentEffect[] BuildDelta(SnowTag parent)
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

        return delta.ToArray();
    }

    public void DeleteGameState(SnowTag stateRef)
    {
        if (CurrentProject == null)
            return;
        if (!CurrentProject.GameStates.TryGetValue(stateRef, out var state))
            return;

        // reject deleting a parent snapshot
        if (CurrentProject.GameStates.Values.Any(g => !g.Deleted && g.Parent == stateRef))
        {
            GD.PrintErr($"Cannot delete GameState '{state.Name}': it has child snapshots.");
            return;
        }

        state.Deleted = true;
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<GameState> { Id = state.Id, Payload = state }
            )
        );
        SaveProject(CurrentProject);
    }

    /// <summary>
    /// Switches every client to a saved game state.
    /// </summary>
    public void SwitchGameState(SnowTag stateRef)
    {
        if (CurrentProject?.GetGameState(stateRef) == null)
            return;

        var effects = new List<Effect>
        {
            new TableClearEffect(),
            new ActiveGameStateEffect { Target = stateRef },
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
                        DataSetRow = s.DataSetRow,
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
        while (
            cursor != SnowTag.Empty
            && CurrentProject != null
            && CurrentProject.GameStates.TryGetValue(cursor, out var gs)
        )
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
            && a.DataSetRow == b.DataSetRow
            && a.ZOrder.Target == b.ZOrder.Target
            && a.ZOrder.Suborder == b.ZOrder.Suborder
            && a.ZOrder.LastEvent == b.ZOrder.LastEvent;
    }

    public void AddPrototypeToManifest(CreateObjectEventArgs args)
    {
        if (CurrentProject == null)
            return;

        if (!CurrentProject.Prototypes.ContainsKey(args.PrototypeRef))
        {
            var newProto = new Prototype { Id = args.PrototypeRef, Parameters = args.Params };

            if (!string.IsNullOrEmpty(args.Params?.ComponentName))
            {
                newProto.Name = args.Params.ComponentName;
            }
            else
            {
                newProto.Name = $"Unnamed {args.ComponentType}";
            }

            UpdatePrototype(newProto);
        }
    }

    public DataSet GetDataSet(SnowTag datasetRef)
    {
        if (datasetRef == SnowTag.Empty || CurrentProject == null)
            return null;
        if (CurrentProject.Datasets.TryGetValue(datasetRef, out var d) && !d.Deleted)
            return d;
        return null;
    }

    public Template GetTemplate(SnowTag templateRef)
    {
        if (templateRef == SnowTag.Empty || CurrentProject == null)
            return null;
        if (CurrentProject.Templates.TryGetValue(templateRef, out var t) && !t.Deleted)
            return t;
        return null;
    }

    private readonly Dictionary<SnowTag, Task> _inFlightFetches = new();

    public async Task FetchImageAsync(Asset asset, Action<Asset> callback)
    {
        if (asset == null)
            return;

        if (asset.AssetDownloaded)
        {
            callback(asset);
            return;
        }

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

        callback(asset);
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

            asset.Image = r.Item2;
            asset.AssetDownloaded = true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Image fetch failed: {ex.Message}");
        }
    }

    public GameObjects GameObjects { get; set; }

    public VisualComponentBase SpawnDisconnectedVisualComponent(
        Prototype prototype,
        string row,
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

        //if the name is blank in the parameters, set it
        if (
            !string.IsNullOrEmpty(prototype.Parameters.BaseName)
            && string.IsNullOrWhiteSpace(prototype.Parameters.ComponentName)
        )
        {
            prototype.Parameters.ComponentName = "unbound";
        }

        if (component.Setup(prototype.Id, row, textureFactory))
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
