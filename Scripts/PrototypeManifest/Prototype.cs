using System.Text.Json.Serialization;

[JsonName("Prototype")]
public record Prototype : Replicated
{
    public static readonly Id<Prototype, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// Gets all prototypes that aren't deleted.
    /// </summary>
    public static readonly All<Prototype> All = new(p => !p.Deleted);

    public string Name { get; init; }

    /// <summary>
    /// Set when the player deletes the prototype.
    /// Components made from it still read it, so it stays in saves while a component refers to it.
    /// </summary>
    public bool Deleted { get; init; }

    public ComponentParameters Parameters { get; init; }

    [JsonIgnore]
    public VisualComponentBase.VisualComponentType Type => Parameters?.ComponentType ?? default;
}
