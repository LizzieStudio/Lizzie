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

    //all strings for now as placeholders
    public Dictionary<string, string> Images { get; set; } = new();

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
}
