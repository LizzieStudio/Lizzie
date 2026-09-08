using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;
using Lizzie.AssetManagement;
using TTSS.Scripts.Templating;

public class Project
{
    public string Name { get; set; }
    public int Version { get; set; }
    public Dictionary<SnowportId, Template> Templates { get; set; } = new();
    public Dictionary<SnowportId, DataSet> Datasets { get; set; } = new();
    public Dictionary<SnowportId, Prototype> Prototypes { get; set; } = new();

    public Dictionary<string, Asset> Images { get; set; } = new();

    /// <summary>
    /// Named scene snapshots the user can save and restore.
    /// Key is the state name.
    /// </summary>
    public Dictionary<string, GameState> GameStates { get; set; } = new();

    /// <summary>
    /// Project-level settings edited via the Project Settings dialog.
    /// </summary>
    public ProjectGameSettings GameSettings { get; set; } = new();

    /// <summary>
    /// List of cloud-stored assets associated with this project
    /// </summary>
    public List<Asset> Assets { get; set; } = new();

    public Template GetTemplate(SnowportId templateRef)
    {
        var t = new Template();
        if (templateRef == SnowportId.Empty)
        {
            return t;
        }

        if (Templates.TryGetValue(templateRef, out var template) && !template.Deleted)
        {
            return template;
        }
        else
        {
            GD.PrintErr($"Template '{templateRef}' not found in project.");
            return t;
        }
    }

    public DataSet GetDataset(SnowportId datasetRef)
    {
        var d = new DataSet();
        if (datasetRef == SnowportId.Empty)
        {
            return d;
        }
        if (Datasets.TryGetValue(datasetRef, out var dataset) && !dataset.Deleted)
        {
            return dataset;
        }
        else
        {
            GD.PrintErr($"Dataset '{datasetRef}' not found in project.");
            return d;
        }
    }
}
