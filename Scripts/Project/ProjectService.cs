using System;
using System.Text.Json;
using Godot;

public partial class ProjectService : Node
{
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
        _currentProject = project;
        _suppressProjectChangeEvent = false;
    }

    public Project LoadProject(string name)
    {
        if (!FileAccess.FileExists($"user://{name}.proj"))
        {
            return null; // Error! We don't have a save to load.
        }

        using var saveFile = FileAccess.Open($"user://{name}.proj", FileAccess.ModeFlags.Read);

        var s = saveFile.GetAsText();

        var p = JsonSerializer.Deserialize<Project>(s);

        /*
        var d = p.Datasets.First().Value;
        d.Columns.Add("IconColor");

        var colors = new string[] { "Red","Blue", "Yellow", "Gray", "Purple", "Green" };
        int i = 0;
        foreach (var dc in d.Rows)
        {
            dc.Value.Data.Add(colors[i++]);
        }
        */
        p.FixDatasetName();

        return p;
    }

    public bool SaveProject(Project project, string fileName)
    {
        using var saveFile = FileAccess.Open($"user://{fileName}.proj", FileAccess.ModeFlags.Write);

        var s = JsonSerializer.Serialize<Project>(project);

        saveFile.StoreString(s);
        saveFile.Close();

        return true;
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
        return project;
    }
}
