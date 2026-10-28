using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Updates blueprint decks with their new contents after a prototype or dataset change.
/// </summary>
/// <remarks>
/// Deck contents are marked with the deck they came from, to make them easy to find.
/// Items that should be kept are updated, removed items are deleted, and new items are added to the VcDeck (if there is one).
/// </remarks>
public static class DeckReconciliation
{
    /// <summary>
    /// The changes that bring every deck of <paramref name="prototype"/> in line with <paramref name="parameters"/>.
    /// </summary>
    /// <param name="parameters">The prototype's parameters, which may be newer than the ones written.</param>
    /// <param name="newId">Makes the Ids of new cards, for testing.</param>
    /// <param name="stamp">Orders new cards under the others, for testing.</param>
    public static List<IChange> Changes(
        IAtomReader R,
        Ref<Prototype> prototype,
        PrintedParameters parameters,
        Func<Ref<Component>> newId = null,
        SnowportId? stamp = null
    )
    {
        var idCreate = newId ?? Ref<Component>.Create;
        var snowportStamp = stamp ?? SnowportId.Create();

        IEnumerable<Stage> Below(Ref<Stage> parent) =>
            R.Get(Stage.Children, parent).SelectMany(stage => Below(stage.Id).Prepend(stage));

        var blueprints = Below(Ref<Stage>.Empty).Where(stage => !stage.IsTable).ToList();
        var wanted = parameters.Items(R);
        var changes = blueprints
            .SelectMany(stage =>
                R.Get(ComponentPosition.ByStage, stage.Id)
                    .Where(c =>
                        R.GetComponent(c) is { IsItem: false } component
                        && component.PrototypeRef == prototype
                        && (
                            stage.ParentRef.IsEmpty
                            || R.Get(
                                ComponentPosition.ById,
                                new StageKey<ComponentPosition>(stage.ParentRef, c.Id)
                            ) == null
                        )
                    )
            )
            .SelectMany(deck => Deck(R, deck, prototype, wanted, idCreate, snowportStamp))
            .ToList();

        var retiring = changes
            .OfType<Component>()
            .Where(c => c.Retired)
            .Select(c => c.Id)
            .ToHashSet();
        bool IsRetired(ComponentPosition c) =>
            retiring.Contains(c.Id)
            || R.GetComponent(c) is { Retired: true } component
                && component.PrototypeRef == prototype;
        changes.AddRange(
            blueprints.SelectMany(stage =>
                R.Get(ComponentPosition.WrittenIn, stage.Id)
                    .Where(c => !c.Removed && IsRetired(c))
                    .Select(c => c with { Removed = true })
            )
        );
        return changes;
    }

    /// <summary>
    /// A new card face-down in <paramref name="deck"/>.
    /// </summary>
    public static Atom[] NewCard(
        ComponentPosition deck,
        Ref<Prototype> prototype,
        PrintedItemRef item,
        Func<Ref<Component>> newId = null,
        ZOrder order = default
    )
    {
        var id = (newId ?? Ref<Component>.Create)();
        return
        [
            new Component
            {
                Id = id,
                PrototypeRef = prototype,
                Item = item,
                DeckRef = deck.Id,
            },
            new ComponentPosition
            {
                Id = id,
                StageRef = deck.StageRef,
                Container = new InComponent(deck.Id),
                Rotation = new Vector3(0, 0, Mathf.Pi),
                ZOrder = order,
            },
        ];
    }

    /// <summary>
    /// Pairs the <paramref name="issued"/> cards with the <paramref name="wanted"/> ones:
    /// <list type="number">
    /// <item>equal items</item>
    /// <item>the same front</item>
    /// <item>the same back</item>
    /// </list>
    /// A side only pairs cards when its key appears once on that side of each list.
    /// Earlier cards pair first.
    /// </summary>
    /// <returns>
    /// The pairs, the issued cards left unpaired, and the wanted cards left unpaired, as indexes into each list.
    /// </returns>
    public static (
        List<(int Issued, int Wanted)> Pairs,
        List<int> Unwanted,
        List<int> Missing
    ) Match(IReadOnlyList<PrintedItemRef> issued, IReadOnlyList<PrintedItemRef> wanted)
    {
        var pairs = new List<(int, int)>();
        var unwanted = Enumerable.Range(0, issued.Count).ToList();
        var missing = Enumerable.Range(0, wanted.Count).ToList();

        void Pair(Func<PrintedItemRef, PrintedItemRef, bool> same)
        {
            foreach (int i in unwanted.ToList())
            {
                int w = missing.FindIndex(w => same(issued[i], wanted[w]));
                if (w >= 0)
                {
                    pairs.Add((i, missing[w]));
                    unwanted.Remove(i);
                    missing.RemoveAt(w);
                }
            }
        }

        bool SameSide(FaceKey a, FaceKey b, Func<PrintedItemRef, FaceKey> side) =>
            a != null
            && Equals(a, b)
            && issued.Count(item => Equals(side(item), a)) == 1
            && wanted.Count(item => Equals(side(item), a)) == 1;

        Pair(Equals);
        Pair((a, b) => SameSide(a.Front, b.Front, item => item.Front));
        Pair((a, b) => SameSide(a.Back, b.Back, item => item.Back));
        return (pairs, unwanted, missing);
    }

    private static IEnumerable<IChange> Deck(
        IAtomReader R,
        ComponentPosition deck,
        Ref<Prototype> prototype,
        IReadOnlyList<PrintedItemRef> wanted,
        Func<Ref<Component>> newId,
        SnowportId stamp
    )
    {
        var held = R.Get(ComponentPosition.ByContainer, deck.Inside).Select(c => c.Id).ToHashSet();

        // The cards out of the deck pair first, so the deck retires the cards it holds.
        var issued = R.Get(Component.ByDeck, deck.Id)
            .OrderBy(card => held.Contains(card.Id))
            .ToList();
        var (pairs, unwanted, missing) = Match(
            issued.Select(card => (PrintedItemRef)card.Item).ToList(),
            wanted
        );

        var changes = new List<IChange>();
        foreach (var (i, w) in pairs)
            if (!Equals(issued[i].Item, wanted[w]))
                changes.Add(issued[i] with { Item = wanted[w] });
        foreach (int i in unwanted)
            changes.Add(issued[i] with { Retired = true });
        // new cards go under the deck's current cards
        int suborder = missing.Count;
        foreach (int w in missing)
            changes.AddRange(
                NewCard(
                    deck,
                    prototype,
                    wanted[w],
                    newId,
                    new ZOrder(ZTarget.Bottom, suborder--, stamp)
                )
            );
        return changes;
    }
}
