using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

public record Template : IReplicated
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

    public SnowTag Id { get; init; }

    /// <summary>
    /// Reversible soft-delete flag.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>The id of the last event that updated this template.</summary>
    public SnowportId LastUpdateId { get; init; }

    public string Name { get; init; }
    public string Description { get; init; }

    public string SizeTemplate { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public ImmutableArray<ImmutableDictionary<string, string>> Elements { get; init; } =
        ImmutableArray<ImmutableDictionary<string, string>>.Empty;

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

    public SnowTag DataSet { get; init; }

    public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
        this with { Id = id, LastUpdateId = lastUpdateId };
}
