using System;
using System.Collections.Generic;
using System.Linq;
using Lizzie.Replication.Machinery;

/// <summary>
/// Reads atoms.
/// </summary>
public interface IAtomReader
{
    /// <summary>
    /// The atom <paramref name="atomRef"/> points at, or null if it is missing or unset.
    /// </summary>
    Atom Get(AtomRef atomRef);

    /// <summary>
    /// The id of the event that last updated <paramref name="atom"/>,
    /// or <see cref="SnowportId.Empty"/> if it was never written.
    /// </summary>
    SnowportId Version(Atom atom);

    /// <summary>
    /// Gets the atom with <paramref name="id"/>, or null if it is missing or unset.
    /// </summary>
    T Get<T, I>(Id<T, I> byId, I id)
        where T : Atom
        where I : IComparable<I>;

    /// <summary>
    /// Gets the atoms with <paramref name="key"/> in <paramref name="group"/> that aren't unset.
    /// </summary>
    IReadOnlyList<T> Get<T, G>(Group<T, G> group, G key)
        where T : Atom;

    /// <summary>
    /// How many atoms with <paramref name="key"/> in <paramref name="group"/> aren't unset.
    /// </summary>
    /// <remarks>
    /// A watch that reads only this reruns only when the count changes, not when the atom values change.
    /// </remarks>
    int Count<T, G>(Group<T, G> group, G key)
        where T : Atom;
}

/// <summary>
/// Used to get atoms and observe them for changes.
/// </summary>
/// <remarks>
/// Only to be used with <see cref="AtomService.Watch"/>.
/// </remarks>
public interface IWatchReader : IAtomReader
{
    /// <summary>
    /// Finds the <paramref name="basis"/> of every atom with <paramref name="key"/> in <paramref name="group"/>,
    /// and compares each atom's basis with the one from the previous Sync.
    /// <list type="bullet">
    /// <item>If the bases differ and the previous one wasn't null, the atom appears in removed, with its previous value and basis.</item>
    /// <item>If the bases differ and the new one isn't null, the atom appears in added, with its new value and basis.</item>
    /// </list>
    /// <para>The expectation is that you will remove the nodes in removed and add the nodes in added.</para>
    /// A null basis means "no child".
    /// </summary>
    GroupDiff<T, V> Diff<T, G, V>(Group<T, G> group, G key, Basis<T, V> basis)
        where T : Atom
        where V : struct;
}

/// <summary>
/// The children a parent should remove, with their previous bases, and the children it should add,
/// from <see cref="IWatchReader.Diff{T, G, V}"/>.
/// </summary>
public readonly record struct GroupDiff<T, V>(
    IReadOnlyList<(T Atom, V Value)> Removed,
    IReadOnlyList<(T Atom, V Value)> Added
);

/// <summary>
/// Helps determine when a Node needs to be rebuilt. If the Basis changes, the Node needs to be replaced.
///
/// <para>
/// In a parent Node, declare a static <see cref="Basis{T, V}"/> and run <see cref="IWatchReader.Diff{T, G, V}"/>.
/// Any atom whose <see cref="Basis{T, V}"/> changes will appear in Removed and Added so that it will be recreated.
/// </para>
///
/// <code>static readonly Basis&lt;ComponentState, (string Scene, SnowTag Proto)&gt; NodeBasis = new((R, s) => ...);</code>
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
    /// The atom <paramref name="atomRef"/> points at,
    /// or what stands in for it before it's written.
    /// Null if it's missing and its type has no stand-in.
    /// </summary>
    /// <remarks>
    /// Commands and relations act on what this gives.
    /// </remarks>
    public static Atom Resolve(this IAtomReader R, AtomRef atomRef) =>
        R.Get(atomRef) ?? IdQuery.For(atomRef.Type).Unwritten(atomRef.Id);

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

    /// <inheritdoc cref="IWatchReader.Diff{T, G, V}"/>
    public static GroupDiff<T, V> Diff<T, V>(this IWatchReader R, All<T> all, Basis<T, V> basis)
        where T : Atom
        where V : struct => R.Diff(all, true, basis);

    /// <summary>
    /// The atoms that left or joined <paramref name="key"/> of <paramref name="group"/> since the previous Sync.
    /// </summary>
    public static GroupDiff<T, bool> Diff<T, G>(this IWatchReader R, Group<T, G> group, G key)
        where T : Atom => R.Diff(group, key, Member<T>.Basis);

    /// <inheritdoc cref="Diff{T, G}(IWatchReader, Group{T, G}, G)"/>
    public static GroupDiff<T, bool> Diff<T>(this IWatchReader R, All<T> all)
        where T : Atom => R.Diff(all, true);

    /// <remarks>
    /// Ignore this. It's just here to provide an "Identity Function" to <see cref="Diff{T, G}"/>.
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
        && sync.Log.TryGetValue(R.Version(atom), out var writer)
            ? writer.Command
            : null;

    /// <summary>
    /// The rows of a dataset in order.
    /// </summary>
    public static List<DataRow> GetRows(this IAtomReader R, SnowTag datasetRef) =>
        Ranked(R, DataRow.ByDataSet, datasetRef);

    /// <summary>
    /// The columns of a dataset in order.
    /// </summary>
    public static List<DataColumn> GetColumns(this IAtomReader R, SnowTag datasetRef) =>
        Ranked(R, DataColumn.ByDataSet, datasetRef);

    private static List<T> Ranked<T>(IAtomReader R, Group<T, SnowTag> group, SnowTag datasetRef)
        where T : Atom, IRanked
    {
        if (datasetRef == SnowTag.Empty)
            return [];

        var items = R.Get(group, datasetRef).ToList();
        items.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        return items;
    }

    /// <summary>
    /// What <paramref name="row"/> holds in each of its dataset's columns, by column name, for a template to show.
    /// If two columns share a name, the first one is used.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Values(this IAtomReader R, DataRow row)
    {
        var values = new Dictionary<string, string>();
        foreach (var column in R.GetColumns(row.DataSetId))
            values.TryAdd(
                column.Name,
                R.Get(DataCell.ById, new CellId(row.Id, column.Id))?.Value ?? string.Empty
            );
        return values;
    }

    /// <summary>
    /// What a component shows as. A deck's cards share the deck's prototype but show as tokens,
    /// as the table does when it picks a component's scene.
    /// </summary>
    public static VisualComponentBase.VisualComponentType Kind(this IAtomReader R, ComponentState s)
    {
        var type =
            R.Get(Prototype.ById, s.PrototypeRef)?.Type
            ?? VisualComponentBase.VisualComponentType.Unset;
        return type == VisualComponentBase.VisualComponentType.Deck && s.IsItem
            ? VisualComponentBase.VisualComponentType.Token
            : type;
    }

    /// <summary>
    /// The cards in a deck, top first.
    /// </summary>
    public static List<ComponentState> TokensOn(this IAtomReader R, ComponentState deck) =>
        R.Get(ComponentState.ByContainer, deck.In(deck.Id))
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
    public static bool IsBeingDragged(this IAtomReader R, ComponentState c) =>
        !c.IsOnTable
        && R.Get(ProjectGameSettings.Only)
            .Players.Any(p => p.CursorRef == c.ContainerRef || p.HandHoldRef == c.ContainerRef);

    /// <summary>
    /// Whether a player is dragging <paramref name="c"/> over their hand, where no one else sees it.
    /// </summary>
    public static bool IsHeldOverHand(this IAtomReader R, ComponentState c) =>
        !c.IsOnTable
        && R.Get(ProjectGameSettings.Only).Players.Any(p => p.HandHoldRef == c.ContainerRef);

    /// <summary>
    /// True if it's inside a container, such as a bag or a player's hand, rather than on the table.
    /// </summary>
    public static bool IsContained(this IAtomReader R, ComponentState c) =>
        !c.IsOnTable && !R.IsBeingDragged(c);

    /// <summary>
    /// Whether <paramref name="c"/> is in a player's hand.
    /// </summary>
    public static bool IsInHand(this IAtomReader R, ComponentState c) =>
        !c.IsOnTable
        && R.Get(ProjectGameSettings.Only).Players.Any(p => p.HandRef == c.ContainerRef);

    /// <summary>
    /// Whether <paramref name="c"/> is a token in a deck.
    /// </summary>
    public static bool IsInDeck(this IAtomReader R, ComponentState c) =>
        !c.IsOnTable
        && R.Get(ComponentState.ById, c.In(c.ContainerRef)) is { } container
        && R.Kind(container) == VisualComponentBase.VisualComponentType.Deck;

    /// <summary>
    /// Whether <paramref name="c"/> is out of sight in a container.
    /// </summary>
    /// <remarks>
    /// As of writing this, only bags do this.
    /// </remarks>
    public static bool IsHidden(this IAtomReader R, ComponentState c) =>
        R.IsContained(c) && !R.IsInDeck(c) && !R.IsInHand(c);
}
