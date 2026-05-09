using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

public partial class ProjectService : Node
{
    public const string SampleProjectName = "Test Project";

    public enum ProjectElement
    {
        Dataset,
        Template,
        Component,
        Image,
    }

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

        /*
        var p = JsonSerializer.Deserialize<Project>(s);

        p.FixDatasetName();
        p?.MapPrototypeJson();    //map the generic JSON objects to what we actually need
        */

        var p = DeserializeProject(s);

        return p;
    }

    public bool SaveProject(Project project)
    {
        using var saveFile = FileAccess.Open(
            $"user://{project.Name}.proj",
            FileAccess.ModeFlags.Write
        );

        var s = JsonSerializer.Serialize<Project>(project);

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
        return JsonSerializer.Serialize(project);
    }

    /// <summary>
    /// Deserialize a project from JSON string for network sync
    /// </summary>
    public Project DeserializeProject(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        var project = JsonSerializer.Deserialize<Project>(json);
        project?.FixDatasetName();
        project?.MapPrototypeJson(); //map the generic JSON objects to what we actually need

        return project;
    }

    public string SerializeDataSet(DataSet dataset)
    {
        if (dataset == null)
            return "{}";
        return JsonSerializer.Serialize(dataset);
    }

    public DataSet DeserializeDataSet(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        var dataset = JsonSerializer.Deserialize<DataSet>(json);
        return dataset;
    }

    public void UpdateDataSet(DataSet dataset)
    {
        if (CurrentProject == null || dataset == null)
            return;
        CurrentProject.Datasets.TryAdd(dataset.Name, dataset);
        CurrentProject.Datasets[dataset.Name] = dataset;
        EventBus.Instance.Publish(
            new DataSetChangedEvent { DataSet = dataset, DataSetName = dataset.Name }
        );
    }

    public void UpdateTemplate(Template template)
    {
        if (CurrentProject == null || template == null)
            return;
        CurrentProject.Templates.TryAdd(template.Name, template);
        CurrentProject.Templates[template.Name] = template;
        EventBus.Instance.Publish(
            new TemplateChangedEvent { Template = template, TemplateName = template.Name }
        );
    }

    public void UpdatePrototype(Prototype prototype)
    {
        if (CurrentProject == null || prototype == null)
            return;
        CurrentProject.Prototypes.TryAdd(prototype.PrototypeRef, prototype);
        CurrentProject.Prototypes[prototype.PrototypeRef] = prototype;
        EventBus.Instance.Publish(
            new PrototypeChangedEvent { PrototypeId = prototype.PrototypeRef }
        );
    }

    public void DeletePrototype(Guid prototypeRef)
    {
        if (CurrentProject == null)
            return;
        CurrentProject.Prototypes.Remove(prototypeRef);
        SaveProject(CurrentProject);
        EventBus.Instance.Publish(new DeletePrototypeEvent { PrototypeRef = prototypeRef });
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
                PrototypeRef = args.PrototypeRef,
                Type = args.ComponentType,
                Parameters = args.Params,
            };

            if (args.Params.ContainsKey("ComponentName"))
            {
                newProto.Name = args.Params["ComponentName"].ToString();
            }
            else
            {
                newProto.Name = $"Unnamed {args.ComponentType}";
            }

            UpdatePrototype(newProto);
        }
    }

    public DataSet GetDataSetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || CurrentProject == null)
            return null;
        if (CurrentProject.Datasets == null)
            return null;
        return CurrentProject.Datasets.GetValueOrDefault(name);
    }

    public Template GetTemplateByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || CurrentProject == null)
            return null;
        if (CurrentProject.Templates == null)
            return null;
        return CurrentProject.Templates.GetValueOrDefault(name);
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
}
