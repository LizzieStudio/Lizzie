using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;
using TTSS.Scripts.Templating;

public class Template
{
    public enum TemplateTarget
    {
        Flat,
        D4,
        D6,
        D8,
        D10,
        D12,
        D20,
    }

    public string Name { get; set; }
    public string Description { get; set; }

    public string SizeTemplate { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public List<Dictionary<string, string>> Elements { get; set; } = new();

    [JsonIgnore]
    public TemplateTarget Target
    {
        get
        {
            switch (SizeTemplate)
            {
                case "D4":
                    return TemplateTarget.D4;
                case "D6":
                    return TemplateTarget.D6;
                case "D8":
                    return TemplateTarget.D8;
                case "D10":
                    return TemplateTarget.D10;
                case "D12":
                    return TemplateTarget.D12;
                case "D20":
                    return TemplateTarget.D20;
            }

            return TemplateTarget.Flat;
        }
    }

    public string DataSet { get; set; } = string.Empty;
}
