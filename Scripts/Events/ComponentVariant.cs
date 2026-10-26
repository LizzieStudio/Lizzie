using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// What a component is. Its prototype and which item from that prototype.
/// </summary>
[JsonName("ComponentVariant")]
public record ComponentVariant : Atom
{
    public static readonly Identity<ComponentVariant, Ref<ComponentVariant>> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Ref{T}.Create"/>.
    /// </summary>
    public required Ref<ComponentVariant> Id { get; init; }

    /// <summary>
    /// The cards each deck issued and hasn't retired, by the deck's Id.
    /// </summary>
    /// <remarks>
    /// A deck keeps these in saves, even after their components are deleted, so it doesn't issue them again.
    /// </remarks>
    public static readonly Lookup<ComponentVariant, SnowTag?> ByDeck = new(v =>
        v.DeckRef == SnowTag.Empty || v.Retired ? null : v.DeckRef
    );

    public Ref<Prototype> PrototypeRef { get; init; }

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
    /// The deck that issued this card, or <see cref="SnowTag.Empty"/> when it didn't come from a deck.
    /// </summary>
    /// <remarks>
    /// Each card a deck issues gets its own variant, so a deck can count the cards it issued.
    /// </remarks>
    public SnowTag DeckRef { get; init; }

    /// <summary>
    /// Set when its deck no longer wants this card, after its prototype changed.
    /// Components of a retired variant still show it until a player deletes them.
    /// </summary>
    public bool Retired { get; init; }

    /// <summary>
    /// A new variant for a node that hasn't been written yet, such as a spawn preview.
    /// </summary>
    public static ComponentVariant Capture(VisualComponentBase c) =>
        new()
        {
            Id = Ref<ComponentVariant>.Create(),
            PrototypeRef = c.PrototypeRef,
            Item = c.Item,
        };

    /// <summary>
    /// Its prototype and, for a card made from a dataset, its rows.
    /// </summary>
    /// <remarks>
    /// This will need to be extended as new references are invented.
    /// </remarks>
    public override IEnumerable<IAtomRef> Referenced(IAtomReader R) =>
        new[] { PrototypeRef }
            .Where(p => !p.IsEmpty)
            .Cast<IAtomRef>()
            .Concat((Item?.Rows() ?? []).Where(r => !r.IsEmpty).Cast<IAtomRef>());
}
