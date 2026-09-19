using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;
using Lizzie.AssetManagement;
using TTSS.Scripts.Templating;

public class Project
{
    public string Filename { get; set; }
    public Dictionary<SnowTag, Template> Templates { get; set; } = new();
    public Dictionary<SnowTag, DataSet> Datasets { get; set; } = new();
    public Dictionary<SnowTag, Prototype> Prototypes { get; set; } = new();

    public Dictionary<SnowTag, Asset> Assets { get; set; } = new();

    /// <summary>
    /// Named scene snapshots the user can save and restore.
    /// Key is the state's immutable id.
    /// </summary>
    public Dictionary<SnowTag, GameState> GameStates { get; set; } = new();

    /// <summary>
    /// The snapshot currently loaded or <see cref="SnowTag.Empty"/>.
    /// </summary>
    public SnowTag ActiveGameState { get; set; }

    /// <summary>
    /// Project-level settings edited via the Project Settings dialog.
    /// </summary>
    public ProjectGameSettings GameSettings { get; set; } = new();

    public Template GetTemplate(SnowTag templateRef)
    {
        var t = new Template();
        if (templateRef == SnowTag.Empty)
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

    public DataSet GetDataset(SnowTag datasetRef)
    {
        var d = new DataSet();
        if (datasetRef == SnowTag.Empty)
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

    public Asset GetImage(SnowTag imageRef)
    {
        if (imageRef == SnowTag.Empty)
            return null;
        if (Assets.TryGetValue(imageRef, out var asset) && !asset.Deleted)
            return asset;
        return null;
    }

    public GameState GetGameState(SnowTag stateRef)
    {
        if (stateRef == SnowTag.Empty)
            return null;
        if (GameStates.TryGetValue(stateRef, out var state) && !state.Deleted)
            return state;
        return null;
    }
}
