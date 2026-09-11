using System;
using System.Collections.Generic;
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
    private bool _suppressProjectChangeEvent = false;

    public Project CurrentProject
    {
        get => _currentProject;
        set
        {
            if (!ReferenceEquals(_currentProject, value))
                TextureCache.Instance.Clear();
            _currentProject = value;
            if (!_suppressProjectChangeEvent)
            {
                EventBus.Instance.Publish<ProjectChangedEvent>(); //no params means everything has changed
                EventBus.Instance.Publish<ProjectSettingsChangedEvent>();
            }
        }
    }

    /// <summary>
    /// Set current project without triggering change event (used for network sync)
    /// </summary>
    public void SetProjectSilent(Project project)
    {
        _suppressProjectChangeEvent = true;
        if (!ReferenceEquals(_currentProject, project))
            TextureCache.Instance.Clear();
        _currentProject = project;
        _suppressProjectChangeEvent = false;
    }

    public Project LoadProject(string name)
    {
        if (!FileAccess.FileExists($"user://{name}.proj"))
        {
            return null; // Error! We don't have a save to load.
        }

        using var loadFile = FileAccess.Open($"user://{name}.proj", FileAccess.ModeFlags.Read);

        var s = loadFile.GetAsText();

        loadFile.Close();

        return DeserializeProject(s);
    }

    public bool SaveProject(Project project)
    {
        using var saveFile = FileAccess.Open(
            $"user://{project.Filename}.proj",
            FileAccess.ModeFlags.Write
        );

        var s = JsonSerializer.Serialize<Project>(project, LizzieJson.Options);

        saveFile.StoreString(s);
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
    /// Serialize a project to JSON string for network sync
    /// </summary>
    public string SerializeProject(Project project)
    {
        if (project == null)
            return "{}";
        return JsonSerializer.Serialize(project, LizzieJson.Options);
    }

    /// <summary>
    /// Deserialize a project from JSON string for network sync
    /// </summary>
    public Project DeserializeProject(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Project>(json, LizzieJson.Options);
        }
        catch (Exception ex)
        {
            // The project will fail to load whenever we introduce breaking changes on the format.
            GD.PrintErr($"Failed to deserialize project: {ex.Message}");
            return null;
        }
    }

    public string SerializeDataSet(DataSet dataset)
    {
        if (dataset == null)
            return "{}";
        return JsonSerializer.Serialize(dataset, LizzieJson.Options);
    }

    public DataSet DeserializeDataSet(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        var dataset = JsonSerializer.Deserialize<DataSet>(json, LizzieJson.Options);
        return dataset;
    }

    public void UpdateDataSet(DataSet dataset)
    {
        if (CurrentProject == null || dataset == null)
            return;
        if (dataset.Id == SnowportId.Empty)
            dataset.Id = Snowport.Clock.Create();
        CurrentProject.Datasets[dataset.Id] = dataset;
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
        if (template.Id == SnowportId.Empty)
            template.Id = Snowport.Clock.Create();
        CurrentProject.Templates[template.Id] = template;
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<Template> { Id = template.Id, Payload = template }
            )
        );
    }

    public void UpdatePrototype(Prototype prototype)
    {
        if (CurrentProject == null || prototype == null)
            return;
        CurrentProject.Prototypes[prototype.Id] = prototype;
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new UpdateReplicatedEffect<Prototype> { Id = prototype.Id, Payload = prototype }
            )
        );
    }

    public void DeletePrototype(SnowportId prototypeRef)
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
        CurrentProject.Images.TryAdd(image.AssetId.ToString(), image);
        CurrentProject.Images[image.AssetId.ToString()] = image;
        EventBus.Instance.Publish(new AssetChangedEvent { Asset = image });
        SaveProject(CurrentProject); // Auto-save on image change
    }

    public void AddPrototypeToManifest(CreateObjectEventArgs args)
    {
        if (CurrentProject == null)
            return;

        if (!CurrentProject.Prototypes.ContainsKey(args.PrototypeRef))
        {
            var newProto = new Prototype
            {
                Id = args.PrototypeRef,
                Parameters = args.Params,
            };

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

    public DataSet GetDataSet(SnowportId datasetRef)
    {
        if (datasetRef == SnowportId.Empty || CurrentProject == null)
            return null;
        if (CurrentProject.Datasets.TryGetValue(datasetRef, out var d) && !d.Deleted)
            return d;
        return null;
    }

    public Template GetTemplate(SnowportId templateRef)
    {
        if (templateRef == SnowportId.Empty || CurrentProject == null)
            return null;
        if (CurrentProject.Templates.TryGetValue(templateRef, out var t) && !t.Deleted)
            return t;
        return null;
    }

    private readonly Dictionary<Guid, Task> _inFlightFetches = new();

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
            if (!_inFlightFetches.TryGetValue(asset.AssetId, out fetch))
            {
                fetch = DownloadAssetAsync(asset);
                _inFlightFetches[asset.AssetId] = fetch;
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
                    _inFlightFetches.TryGetValue(asset.AssetId, out var current)
                    && current == fetch
                    && fetch.IsCompleted
                )
                {
                    _inFlightFetches.Remove(asset.AssetId);
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
