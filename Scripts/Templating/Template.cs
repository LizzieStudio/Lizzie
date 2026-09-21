using System.Collections.Immutable;
using System.Text.Json.Serialization;

public record Template : Replicated
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

    public string Name { get; init; }

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
}
