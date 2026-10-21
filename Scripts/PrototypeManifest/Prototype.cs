using System.Text.Json.Serialization;

namespace Lizzie;

[JsonName("Prototype")]
public record Prototype : Atom
{
    public static readonly Identity<Prototype, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="SnowTag.Create"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// Gets all prototypes that aren't deleted.
    /// </summary>
    public static readonly All<Prototype> Live = new(p => !p.Deleted);

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
