using System;
using System.Collections.Generic;
using System.Linq;
using Lizzie.Replication.Machinery;

/// <summary>
/// Reads atoms.
/// </summary>
public interface IRecordReader
{
    /// <summary>
    /// The record, or null if it is missing or unset.
    /// </summary>
    T Get<T>(SnowTag id)
        where T : Replicated;

    /// <summary>
    /// The record, or null if it is missing or unset.
    /// </summary>
    Replicated Get(SnowTag id);

    /// <summary>
    /// The id of the event that last updated <paramref name="atom"/>,
    /// or <see cref="SnowportId.Empty"/> if it was never written.
    /// </summary>
    SnowportId Version(Replicated atom);

    /// <summary>
    /// The one record of a <see cref="SingletonAttribute"/> type, like the project settings.
    ///
    /// <para>
    /// Table setup creates it, so it should always exist.
    /// Throws if there's none or several.
    /// </para>
    /// </summary>
    T Single<T>()
        where T : Replicated;

    /// <summary>
    /// Gets the atom with <paramref name="id"/>, or null if it is missing or unset.
    /// </summary>
    T Get<T, I>(Id<T, I> byId, I id)
        where T : Replicated;

    /// <summary>
    /// Gets the atoms with <paramref name="key"/> in <paramref name="group"/> that aren't unset.
    /// </summary>
    IReadOnlyList<T> Get<T, G>(Group<T, G> group, G key)
        where T : Replicated;

    /// <summary>
    /// How many atoms with <paramref name="key"/> in <paramref name="group"/> aren't unset.
    /// </summary>
    /// <remarks>
    /// A watch that reads only this reruns only when the count changes, not when the atom values change.
    /// </remarks>
    int Count<T, G>(Group<T, G> group, G key)
        where T : Replicated;

    /// <summary>
    /// Maps every atom with <paramref name="key"/> in <paramref name="group"/> through <paramref name="project"/>,
    /// and compares each atom's result with the one from the previous Sync.
    /// <list type="bullet">
    /// <item>If the results differ and the previous one wasn't null, the atom appears in removed, with its previous value and result.</item>
    /// <item>If the results differ and the new one isn't null, the atom appears in added, with its new value and result.</item>
    /// </list>
    /// <para>The expectation is that you will remove the nodes in removed and add the nodes in added.</para>
    /// When <paramref name="project"/> returns null, this effectively means "no child".
    /// <strong>Only one call of Diff per group key per Sync.</strong>
    /// </summary>
    GroupDiff<T, V> Diff<T, G, V>(Group<T, G> group, G key, Func<IRecordReader, T, V?> project)
        where T : Replicated
        where V : struct;
}

/// <summary>
/// The children a parent should remove, as last projected, and the children it should add,
/// from <see cref="IRecordReader.Diff{T, G, V}"/>.
/// </summary>
public readonly record struct GroupDiff<T, V>(
    IReadOnlyList<(T Atom, V Value)> Removed,
    IReadOnlyList<(T Atom, V Value)> Added
);

public static class RecordReaderExtensions
{
    /// <summary>
    /// The atoms <paramref name="all"/> lists.
    /// </summary>
    public static IReadOnlyList<T> Get<T>(this IRecordReader R, All<T> all)
        where T : Replicated => R.Get(all, true);

    /// <summary>
    /// How many atoms <paramref name="all"/> lists.
    /// </summary>
    public static int Count<T>(this IRecordReader R, All<T> all)
        where T : Replicated => R.Count(all, true);

    /// <inheritdoc cref="IRecordReader.Diff{T, G, V}"/>
    public static GroupDiff<T, V> Diff<T, V>(
        this IRecordReader R,
        All<T> all,
        Func<IRecordReader, T, V?> project
    )
        where T : Replicated
        where V : struct => R.Diff(all, true, project);

    /// <summary>
    /// The atoms that left or joined <paramref name="key"/> of <paramref name="group"/> since the previous Sync.
    /// <strong>Only one call of Diff per group key per Sync.</strong>
    /// </summary>
    public static GroupDiff<T, bool> Diff<T, G>(this IRecordReader R, Group<T, G> group, G key)
        where T : Replicated => R.Diff<T, G, bool>(group, key, (_, _) => true);

    /// <inheritdoc cref="Diff{T, G}(IRecordReader, Group{T, G}, G)"/>
    public static GroupDiff<T, bool> Diff<T>(this IRecordReader R, All<T> all)
        where T : Replicated => R.Diff(all, true);

    /// <summary>
    /// The change that unsets <paramref name="atom"/>, to write like a record.
    /// </summary>
    public static Unset Unset(this IRecordReader R, Replicated atom) => new(atom);

    /// <summary>
    /// A cache key for each <paramref name="atom"/> that changes whenever it changes.
    /// </summary>
    public static string VersionKey(this IRecordReader R, Replicated atom) =>
        $"{AtomId.For(atom.GetType()).Of(atom)}@{R.Version(atom)}";

    /// <summary>
    /// The command that last wrote <paramref name="record"/>'s current state, or null when there was no command attached.
    /// If the last command was an undo or a redo, this falls back to the last effective command (before any reversed event).
    /// </summary>
    public static CommandName? WrittenBy(this IRecordReader R, Replicated record) =>
        EventSynchronizer.Instance is { } sync
        && sync.Log.TryGetValue(R.Version(record), out var writer)
            ? writer.Command
            : null;

    /// <summary>The rows of a dataset in order.</summary>
    public static List<DataRow> GetRows(this IRecordReader R, SnowTag datasetRef)
    {
        if (datasetRef == SnowTag.Empty)
            return new List<DataRow>();

        var rows = R.Get(DataRow.ByDataSet, datasetRef).ToList();
        rows.Sort((a, b) => RowRank.Comparer.Compare(a.Rank, b.Rank));
        return rows;
    }

    /// <summary>
    /// A rank for a new row right after <paramref name="row"/>, or right before it, made by the local player.
    /// It goes between <paramref name="row"/> and its neighbor on that side.
    /// </summary>
    public static string RankBeside(this IRecordReader R, DataRow row, bool after)
    {
        var rows = R.GetRows(row.DataSetId);
        int i = rows.FindIndex(r => r.Id == row.Id);
        return after
            ? RowRank.New(row.Rank, rows.ElementAtOrDefault(i + 1)?.Rank, Snowport.Clock.Create())
            : RowRank.New(rows.ElementAtOrDefault(i - 1)?.Rank, row.Rank, Snowport.Clock.Create());
    }

    /// <summary>
    /// The rank of a dataset's last row, or null if it has none.
    /// </summary>
    public static string LastRank(this IRecordReader R, SnowTag datasetRef) =>
        R.Get(DataRow.ByDataSet, datasetRef).Select(r => r.Rank).Max(RowRank.Comparer);

    /// <summary>
    /// True if <paramref name="id"/> is of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to check against.</typeparam>
    /// <param name="id">The record to check.</param>
    /// <returns>Whether or not <paramref name="id"/> is of type <typeparamref name="T"/></returns>
    public static bool Is<T>(this IRecordReader R, SnowTag id)
        where T : Replicated => R.Get<T>(id) != null;

    /// <summary>
    /// What a component shows as. A deck's cards share the deck's prototype but show as tokens,
    /// as the table does when it picks a component's scene.
    /// </summary>
    public static VisualComponentBase.VisualComponentType Kind(
        this IRecordReader R,
        ComponentState s
    )
    {
        var type =
            R.Get(Prototype.ById, s.PrototypeRef)?.Type
            ?? VisualComponentBase.VisualComponentType.Unset;
        return type == VisualComponentBase.VisualComponentType.Deck && s.IsItem
            ? VisualComponentBase.VisualComponentType.Token
            : type;
    }

    /// <summary>The VcTokens stacked exactly on a deck, top first.</summary>
    public static List<ComponentState> TokensOn(this IRecordReader R, ComponentState deck) =>
        R.Get(ComponentState.OnTableAt, (deck.X, deck.Z))
            .Where(s =>
                s.ZOrder > deck.ZOrder && R.Kind(s) == VisualComponentBase.VisualComponentType.Token
            )
            .OrderByDescending(s => s.ZOrder)
            .ToList();

    /// <summary>
    /// The containers each seat drags with.
    /// <list type="bullet">
    /// <item>Its container for dragging on the table.</item>
    /// <item>Its container for dragging over the hand.</item>
    /// </list>
    /// </summary>
    public static HashSet<SnowTag> Cursors(this IRecordReader R) =>
        R.Single<ProjectGameSettings>()
            .Players.SelectMany(p => new[] { p.CursorRef, p.HandHoldRef })
            .Where(c => c != SnowTag.Empty)
            .ToHashSet();

    /// <summary>
    /// Whether a player is dragging <paramref name="c"/>.
    /// </summary>
    public static bool IsBeingDragged(this IRecordReader R, ComponentState c) =>
        !c.IsOnTable
        && R.Single<ProjectGameSettings>()
            .Players.Any(p => p.CursorRef == c.ContainerRef || p.HandHoldRef == c.ContainerRef);

    /// <summary>
    /// Whether a player is dragging <paramref name="c"/> over their hand, where no one else sees it.
    /// </summary>
    public static bool IsHeldOverHand(this IRecordReader R, ComponentState c) =>
        !c.IsOnTable
        && R.Single<ProjectGameSettings>().Players.Any(p => p.HandHoldRef == c.ContainerRef);

    /// <summary>
    /// True if it's inside a container, such as a bag or a player's hand, rather than on the table.
    /// </summary>
    public static bool IsContained(this IRecordReader R, ComponentState c) =>
        !c.IsOnTable && !R.IsBeingDragged(c);

    /// <summary>
    /// Whether <paramref name="c"/> is in a player's hand.
    /// </summary>
    public static bool IsInHand(this IRecordReader R, ComponentState c) =>
        !c.IsOnTable
        && R.Single<ProjectGameSettings>().Players.Any(p => p.HandRef == c.ContainerRef);
}
