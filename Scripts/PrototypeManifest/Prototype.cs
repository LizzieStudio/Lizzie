using System.Text.Json.Serialization;

[JsonName("Prototype")]
public record Prototype : Replicated
{
    public static readonly Id<Prototype, SnowTag> ById = new(r => r.Id);

    public string Name { get; init; }

    public ComponentParameters Parameters { get; init; }

    [JsonIgnore]
    public VisualComponentBase.VisualComponentType Type => Parameters?.ComponentType ?? default;
}
