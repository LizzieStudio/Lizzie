using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// One component.
/// 
/// <para>
/// This atom represents the components unchanging state, like its prototype and which card inside that prototype.
/// The <see cref="ComponentPosition"/>s, which share its tag, then sets its position in different <see cref="Stage"/>s.
/// </summary>
[JsonName("Component")]
public record Component : Atom
{
    public static readonly Identity<Component, Ref<Component>> ById = new(r => r.Id);

    /// <summary>
    /// The component's tag, which its <see cref="ComponentPosition"/>s share.
    /// </summary>
    public required Ref<Component> Id { get; init; }

    /// <summary>
    /// The cards each deck issued and hasn't retired, by the deck's Id.
    /// </summary>
    /// <remarks>
    /// A deck keeps these in saves, even after the cards are deleted from every stage, so it doesn't issue them again.
    /// </remarks>
    public static readonly Lookup<Component, Ref<Component>?> ByDeck = new(c =>
        c.DeckRef.IsEmpty || c.Retired ? null : c.DeckRef
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
    /// The deck that issued this card, or <see cref="Ref{T}.Empty"/> when it didn't come from a deck.
    /// </summary>
    public Ref<Component> DeckRef { get; init; }

    /// <summary>
    /// Set when its deck no longer wants this card, after its prototype changed.
    /// A retired card still shows until a player deletes it.
    /// </summary>
    public bool Retired { get; init; }

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
