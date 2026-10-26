using System;
using System.Collections.Generic;
using System.Linq;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// Reads atoms.
/// </summary>
public interface IAtomReader
{
    /// <summary>
    /// The atom <paramref name="atomRef"/> points at, or null if it is missing or unset.
    /// </summary>
    Atom Get(IAtomRef atomRef);

    /// <summary>
    /// The id of the event that last updated <paramref name="atom"/>,
    /// or <see cref="SnowportId.Empty"/> if it was never written.
    /// </summary>
    SnowportId Version(Atom atom);

    /// <summary>
    /// Gets the atom with <paramref name="id"/>, or null if it is missing or unset.
    /// </summary>
    T Get<T, I>(Identity<T, I> byId, I id)
        where T : Atom
        where I : IAtomRef<T>;

    /// <summary>
    /// Gets the atoms with <paramref name="key"/> in <paramref name="lookup"/> that aren't unset.
    /// </summary>
    IReadOnlyList<T> Get<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom;

    /// <summary>
    /// How many atoms with <paramref name="key"/> in <paramref name="lookup"/> aren't unset.
    /// </summary>
    /// <remarks>
    /// A watch that reads only this reruns only when the count changes, not when the atom values change.
    /// </remarks>
    int Count<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom;
}

/// <summary>
/// Used to get atoms and observe them for changes.
/// </summary>
/// <remarks>
/// Only to be used with <see cref="Atoms.Watch"/>.
/// </remarks>
public interface IWatchReader : IAtomReader
{
    /// <summary>
    /// Finds the <paramref name="basis"/> of every atom with <paramref name="key"/> in <paramref name="lookup"/>,
    /// and compares each atom's basis with the one from the previous Sync.
    /// <list type="bullet">
    /// <item>If the bases differ and the previous one wasn't null, the atom appears in removed, with its previous value and basis.</item>
    /// <item>If the bases differ and the new one isn't null, the atom appears in added, with its new value and basis.</item>
    /// </list>
    /// <para>The expectation is that you will remove the nodes in removed and add the nodes in added.</para>
    /// A null basis means "no child".
    /// </summary>
    LookupDiff<(T Atom, V Value)> Diff<T, K, V>(Lookup<T, K> lookup, K key, Basis<T, V> basis)
        where T : Atom
        where V : struct;
}

/// <summary>
/// The children a parent should remove and the children it should add, from a Diff.
/// With a basis, each entry pairs an atom with its basis: the previous one in Removed, the new one in Added.
/// Without one, each entry is just the atom.
/// </summary>
public readonly record struct LookupDiff<E>(IReadOnlyList<E> Removed, IReadOnlyList<E> Added);

/// <summary>
/// Helps determine when a Node needs to be rebuilt. If the Basis changes, the Node needs to be replaced.
///
/// <para>
/// In a parent Node, declare a static <see cref="Basis{T, V}"/> and run <see cref="IWatchReader.Diff{T, K, V}"/>.
/// Any atom whose <see cref="Basis{T, V}"/> changes will appear in Removed and Added so that it will be recreated.
/// </para>
///
/// <code>static readonly Basis&lt;ComponentPosition, (string Scene, SnowTag Proto)&gt; NodeBasis = new((R, s) => ...);</code>
/// </summary>
/// <param name="of">An atom's basis, or null for no child. It may read anything through R.</param>
public sealed class Basis<T, V>(Func<IAtomReader, T, V?> of)
    where T : Atom
    where V : struct
{
    internal V? Of(IAtomReader R, T atom) => of(R, atom);
}

public static class AtomReaderExtensions
{
    /// <summary>
    /// The one atom of a singleton type, or a new one with its defaults until the first write.
    /// </summary>
    public static T Get<T>(this IAtomReader R, Singleton<T> only)
        where T : Atom, new() => R.Get(only, default) ?? only.Unwritten(default);

    /// <summary>
    /// Gets the <paramref name="value"/> and subscribes to changes when <paramref name="R"/> is a Watch reader.
    /// </summary>
    public static T Get<T>(this IAtomReader R, WatchableValue<T> value) => value.Read(R);

    /// <summary>
    /// The atom <paramref name="atomRef"/> points at,
    /// or what stands in for it before it's written.
    /// Null if it's missing and its type has no stand-in.
    /// </summary>
    /// <remarks>
    /// Commands and relations act on what this gives.
    /// </remarks>
    public static Atom Resolve(this IAtomReader R, IAtomRef atomRef) =>
        R.Get(atomRef) ?? IdQuery.For(atomRef.AtomType).Unwritten(atomRef);

    /// <summary>
    /// The atoms <paramref name="all"/> lists.
    /// </summary>
    public static IReadOnlyList<T> Get<T>(this IAtomReader R, All<T> all)
        where T : Atom => R.Get(all, true);

    /// <summary>
    /// How many atoms <paramref name="all"/> lists.
    /// </summary>
    public static int Count<T>(this IAtomReader R, All<T> all)
        where T : Atom => R.Count(all, true);

    /// <inheritdoc cref="IWatchReader.Diff{T, K, V}"/>
    public static LookupDiff<(T Atom, V Value)> Diff<T, V>(
        this IWatchReader R,
        All<T> all,
        Basis<T, V> basis
    )
        where T : Atom
        where V : struct => R.Diff(all, true, basis);

    /// <summary>
    /// The atoms that left or joined <paramref name="key"/> of <paramref name="lookup"/> since the previous Sync.
    /// </summary>
    public static LookupDiff<T> Diff<T, K>(this IWatchReader R, Lookup<T, K> lookup, K key)
        where T : Atom
    {
        var (removed, added) = R.Diff(lookup, key, Member<T>.Basis);
        return new(removed.Select(e => e.Atom).ToList(), added.Select(e => e.Atom).ToList());
    }

    /// <inheritdoc cref="Diff{T, K}(IWatchReader, Lookup{T, K}, K)"/>
    public static LookupDiff<T> Diff<T>(this IWatchReader R, All<T> all)
        where T : Atom => R.Diff(all, true);

    /// <remarks>
    /// The basis for <see cref="Diff{T, K}"/>. Every atom has the same basis,
    /// so only atoms that leave or join appear in the diff.
    /// </remarks>
    private static class Member<T>
        where T : Atom
    {
        public static readonly Basis<T, bool> Basis = new((_, _) => true);
    }

    /// <summary>
    /// The change that unsets <paramref name="atom"/>, to write like an atom.
    /// </summary>
    public static Unset Unset(this IAtomReader R, Atom atom) => new(atom);

    /// <summary>
    /// A cache key for each <paramref name="atom"/> that changes whenever it changes.
    /// </summary>
    public static string VersionKey(this IAtomReader R, Atom atom) =>
        $"{IdQuery.For(atom.GetType()).Of(atom)}@{R.Version(atom)}";

    /// <summary>
    /// The command that last wrote <paramref name="atom"/>'s current state, or null when there was no command attached.
    /// If the last command was an undo or a redo, this falls back to the last effective command (before any reversed event).
    /// </summary>
    public static CommandName? WrittenBy(this IAtomReader R, Atom atom) =>
        EventSynchronizer.Instance is { } sync
        && sync.Timeline.TryGetValue(R.Version(atom), out var writer)
            ? writer.Command
            : null;

    /// <summary>
    /// The rows of a dataset that aren't deleted, in order.
    /// </summary>
    public static List<DataRow> GetRows(this IAtomReader R, Ref<Dataset> datasetRef) =>
        datasetRef.IsEmpty ? [] : Ranked(R.Get(DataRow.ByDataset, datasetRef));

    /// <summary>
    /// The columns of a dataset in order.
    /// </summary>
    public static List<DataColumn> GetColumns(this IAtomReader R, Ref<Dataset> datasetRef) =>
        datasetRef.IsEmpty ? [] : Ranked(R.Get(DataColumn.ByDataset, datasetRef));

    private static List<T> Ranked<T>(IEnumerable<T> items)
        where T : IRanked
    {
        var ranked = items.ToList();
        ranked.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        return ranked;
    }

    /// <summary>
    /// What <paramref name="row"/> holds in each of its dataset's columns, by column name, for a template to show.
    /// If two columns share a name, the first one is used.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Values(this IAtomReader R, DataRow row)
    {
        var values = new Dictionary<string, string>();
        foreach (var column in R.GetColumns(row.DatasetRef))
            values.TryAdd(
                column.Name,
                R.Get(DataCell.ById, new CellId(row.Id, column.Id))?.Value ?? string.Empty
            );
        return values;
    }

    /// <summary>
    /// Gets the <see cref="Component"/> that <paramref name="s"/> places, which shares its tag.
    /// </summary>
    public static Component GetComponent(this IAtomReader R, ComponentPosition s) =>
        R.Get(Component.ById, new(s.Id));

    /// <summary>
    /// Gets the <see cref="Prototype"/> for <paramref name="s"/>, or null.
    /// </summary>
    public static Prototype GetPrototype(this IAtomReader R, ComponentPosition s) =>
        R.Get(Prototype.ById, R.GetComponent(s).PrototypeRef);

    /// <summary>
    /// What a component shows as. A printed prototype's decks share its prototype with its cards,
    /// but aren't one of its items, so they show as decks,
    /// as the StageView does when it picks a component's scene.
    /// </summary>
    public static VisualComponentBase.VisualComponentType Kind(
        this IAtomReader R,
        ComponentPosition s
    )
    {
        var type = R.GetPrototype(s)?.Type ?? VisualComponentBase.VisualComponentType.Unset;
        return type == VisualComponentBase.VisualComponentType.Printed && !R.GetComponent(s).IsItem
            ? VisualComponentBase.VisualComponentType.Deck
            : type;
    }

    /// <summary>
    /// The cards in a deck, top first.
    /// </summary>
    public static List<ComponentPosition> TokensOn(this IAtomReader R, ComponentPosition deck) =>
        R.Get(ComponentPosition.ByContainer, deck.In(deck.Id))
            .OrderByDescending(s => s.ZOrder)
            .ToList();

    /// <summary>
    /// The containers each seat drags with.
    /// <list type="bullet">
    /// <item>Its container for dragging on the table.</item>
    /// <item>Its container for dragging over the hand.</item>
    /// </list>
    /// </summary>
    public static HashSet<SnowTag> Cursors(this IAtomReader R) =>
        R.Get(ProjectGameSettings.Only)
            .Players.SelectMany(p => new[] { p.CursorRef, p.HandHoldRef })
            .Where(c => c != SnowTag.Empty)
            .ToHashSet();

    /// <summary>
    /// Whether a player is dragging <paramref name="c"/>.
    /// </summary>
    public static bool IsBeingDragged(this IAtomReader R, ComponentPosition c) =>
        !c.IsOnTable
        && R.Get(ProjectGameSettings.Only)
            .Players.Any(p => p.CursorRef == c.ContainerRef || p.HandHoldRef == c.ContainerRef);

    /// <summary>
    /// Whether a player is dragging <paramref name="c"/> over their hand, where no one else sees it.
    /// </summary>
    public static bool IsHeldOverHand(this IAtomReader R, ComponentPosition c) =>
        !c.IsOnTable
        && R.Get(ProjectGameSettings.Only).Players.Any(p => p.HandHoldRef == c.ContainerRef);

    /// <summary>
    /// True if it's inside a container, such as a bag or a player's hand, rather than on the table.
    /// </summary>
    public static bool IsContained(this IAtomReader R, ComponentPosition c) =>
        !c.IsOnTable && !R.IsBeingDragged(c);

    /// <summary>
    /// Whether <paramref name="c"/> is in a player's hand.
    /// </summary>
    public static bool IsInHand(this IAtomReader R, ComponentPosition c) =>
        !c.IsOnTable
        && R.Get(ProjectGameSettings.Only).Players.Any(p => p.HandRef == c.ContainerRef);

    /// <summary>
    /// Whether <paramref name="c"/> is a card in a deck.
    /// </summary>
    public static bool IsInDeck(this IAtomReader R, ComponentPosition c) =>
        !c.IsOnTable
        && R.Get(ComponentPosition.ById, c.In(c.ContainerRef)) is { } container
        && R.Kind(container) == VisualComponentBase.VisualComponentType.Deck;

    /// <summary>
    /// Whether <paramref name="c"/> is out of sight in a container.
    /// </summary>
    /// <remarks>
    /// As of writing this, only bags do this.
    /// </remarks>
    public static bool IsHidden(this IAtomReader R, ComponentPosition c) =>
        R.IsContained(c) && !R.IsInDeck(c) && !R.IsInHand(c);
}
