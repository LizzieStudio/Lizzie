using System;
using System.Collections.Generic;
using Godot;
using TTSS.Scripts.Templating;

public class Template
{
    public string Name { get; set; }
    public string Description { get; set; }

    public string SizeTemplate { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public List<Dictionary<string, string>> Elements { get; set; } = new();

    public string DataSet { get; set; } = string.Empty;
}
