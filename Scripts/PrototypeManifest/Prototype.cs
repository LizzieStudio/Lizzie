using System.Text.Json.Serialization;

public record Prototype : Replicated
{
    public string Name { get; init; }

    public ComponentParameters Parameters { get; init; }

    [JsonIgnore]
    public VisualComponentBase.VisualComponentType Type => Parameters?.ComponentType ?? default;
}
