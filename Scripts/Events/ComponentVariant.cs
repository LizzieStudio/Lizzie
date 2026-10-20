using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

/// <summary>
/// What a component is. Its prototype and which item from that prototype.
/// </summary>
[JsonName("ComponentVariant")]
public record ComponentVariant : Atom
{
    public static readonly Id<ComponentVariant, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    public SnowTag PrototypeRef { get; init; }

    /// <summary>
    /// Which of its prototype's items it is, or null if it isn't one.
    /// </summary>
    public PrototypeItemRef Item { get; init; }

    /// <summary>
    /// Whether it's one of its prototype's printed items.
    ///
    /// <list type="bullet">
    /// <item>a printed card, in a deck or not, but not the deck itself</item>
    /// <item>a die spawned for a dataset row</item>
    /// </list>
    /// </summary>
    [JsonIgnore]
    public bool IsItem => Item != null;

    /// <summary>
    /// A new variant for a node that hasn't been written yet, such as a spawn preview.
    /// </summary>
    public static ComponentVariant Capture(VisualComponentBase c) =>
        new()
        {
            Id = Snowport.Clock.CreateTag(),
            PrototypeRef = c.PrototypeRef,
            Item = c.Item,
        };

    /// <summary>
    /// Its prototype and, for a card made from a dataset, its rows.
    /// </summary>
    /// <remarks>
    /// This will need to be extended as new references are invented.
    /// </remarks>
    public override IEnumerable<AtomRef> Referenced(IAtomReader R) =>
        new[] { Prototype.ById.Ref(PrototypeRef) }
            .Concat((Item?.Rows() ?? []).Select(DataRow.ById.Ref))
            .Where(r => (SnowTag)r.Id != SnowTag.Empty);
}
