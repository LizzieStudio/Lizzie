using System;
using System.Collections.Generic;
using Godot;
using Lizzie.AssetManagement;
using TTSS.Scripts.Templating;

public class Project
{
    public string Name { get; set; }
    public int Version { get; set; }
    public Dictionary<string, Template> Templates { get; set; } = new();
    public Dictionary<string, DataSet> Datasets { get; set; } = new();
    public Dictionary<Guid, Prototype> Prototypes { get; set; } = new();

    public Dictionary<string, Asset> Images { get; set; } = new();

    /// <summary>
    /// List of cloud-stored assets associated with this project
    /// </summary>
    public List<Asset> Assets { get; set; } = new();

    public void FixDatasetName()
    {
        foreach (var kv in Datasets)
        {
            kv.Value.Name = kv.Key;
        }
    }

    public Template GetTemplate(string name)
    {
        var t = new Template();
        if (string.IsNullOrEmpty(name))
        {
            return t;
        }

        if (Templates.TryGetValue(name, out var template))
        {
            return template;
        }
        else
        {
            GD.PrintErr($"Template '{name}' not found in project.");
            return t;
        }
    }

    public DataSet GetDataset(string name)
    {
        var d = new DataSet();
        if (string.IsNullOrEmpty(name))
        {
            return d;
        }
        if (Datasets.TryGetValue(name, out var dataset))
        {
            return dataset;
        }
        else
        {
            GD.PrintErr($"Dataset '{name}' not found in project.");
            return d;
        }
    }
}
