using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reads project records.
/// Soft-deleted records are only returned by <see cref="GetIncludingDeleted{T}"/>.
/// A reader passed to a watched Sync records every read as a dependency.
/// </summary>
public interface IRecordReader
{
    /// <summary>The record, or null if it is missing or deleted.</summary>
    T Get<T>(SnowTag id)
        where T : class, IReplicated;

    /// <summary>
    /// The record even if it is deleted.
    /// Returns null if the record never existed or if its creation was reversed through an undo action.
    /// </summary>
    T GetIncludingDeleted<T>(SnowTag id)
        where T : class, IReplicated;

    /// <summary>The records that exist, in the order of <paramref name="ids"/>.</summary>
    IReadOnlyList<T> Get<T>(IEnumerable<SnowTag> ids)
        where T : class, IReplicated;

    /// <summary>Every record that passes <paramref name="filter"/>, which must only read the record.</summary>
    IReadOnlyList<T> Get<T>(Func<T, bool> filter)
        where T : class, IReplicated;

    /// <summary>Every record, including ones added later.</summary>
    IReadOnlyList<T> Get<T>()
        where T : class, IReplicated;

    /// <summary>The current value of a <see cref="ReplicatedValue{T}"/>.</summary>
    T Value<T>()
        where T : class;

    /// <summary>
    /// <para>
    /// Maps every live record of <typeparamref name="T"/> to a key, then uses those keys to produce two lists of SnowTags and keys: deletions and creations.
    /// Deletions carry the previous key and creations carry the new one.
    /// To determine presence in a list, the key for each record is compared with the previous key generated for that record.
    /// </para>
    /// <list type="bullet">
    /// <item>If the two keys are different and the previous key wasn't null, the record appears in deletions.</item>
    /// <item>If the two keys are different and the new key isn't null, the record appears in creations.</item>
    /// </list>
    /// <para>The expectation is that you will delete the nodes in deletions and create the nodes in creations.</para>
    /// When <paramref name="keyFn"/> returns null, this effectively means "no child".
    /// If both projections returned a non-null key but the keys are different, that record appears in both deletions and creations.
    /// <strong>Only one call of GetChanged per record type per Sync.</strong>
    /// </summary>
    SwapLists<K> GetChanged<T, K>(Func<IRecordReader, T, K?> keyFn)
        where T : class, IReplicated
        where K : struct;

    /// <summary>
    /// <para>
    /// Produces two lists of SnowTags: deletions and creations.
    /// A record that existed previously but was since deleted or undone will be included in deletions.
    /// A record that did not exist or was deleted previously will be included in creations.
    /// </para>
    /// <para>The expectation is that you will delete the nodes in deletions and create the nodes in creations.</para>
    /// <strong>Only one call of GetChanged per record type per Sync.</strong>
    /// </summary>
    SwapLists GetChanged<T>()
        where T : class, IReplicated;
}

/// <summary>
/// The children a parent should delete and the children a parent should create, from <see cref="IRecordReader.GetChanged{T}"/>.
/// </summary>
public readonly record struct SwapLists(
    IReadOnlyList<SnowTag> Deleted,
    IReadOnlyList<SnowTag> Created
);

/// <summary>
/// The children a parent should delete, with their previous keys, and the children a parent
/// should create, with their new keys, from <see cref="IRecordReader.GetChanged{T, K}"/>.
/// </summary>
public readonly record struct SwapLists<K>(
    IReadOnlyList<(SnowTag Id, K Key)> Deleted,
    IReadOnlyList<(SnowTag Id, K Key)> Created
);

public static class RecordReaderExtensions
{
    /// <summary>The rows of a dataset in order.</summary>
    public static List<DataRow> GetRows(this IRecordReader R, SnowTag datasetRef)
    {
        if (datasetRef == SnowTag.Empty)
            return new List<DataRow>();

        var rows = R.Get<DataRow>(r => r.DataSetId == datasetRef).ToList();
        rows.Sort(
            (a, b) =>
            {
                int c = RowRank.Comparer.Compare(a.Rank, b.Rank);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            }
        );
        return rows;
    }

    /// <summary>
    /// True if <paramref name="id"/> is of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to check against.</typeparam>
    /// <param name="id">The record to check.</param>
    /// <returns>Whether or not <paramref name="id"/> is of type <typeparamref name="T"/></returns>
    public static bool Is<T>(this IRecordReader R, SnowTag id)
        where T : class, IReplicated => R.GetIncludingDeleted<T>(id) != null;

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
            R.GetIncludingDeleted<Prototype>(s.PrototypeRef)?.Type
            ?? VisualComponentBase.VisualComponentType.Unset;
        return type == VisualComponentBase.VisualComponentType.Deck && s.IsCard
            ? VisualComponentBase.VisualComponentType.Token
            : type;
    }

    /// <summary>The VcTokens stacked exactly on a deck, top first.</summary>
    public static List<ComponentState> TokensOn(this IRecordReader R, ComponentState deck) =>
        R.Get<ComponentState>(s =>
                s.Location == VisualComponentBase.ComponentLocation.Table
                && s.X == deck.X
                && s.Z == deck.Z
                && s.ZOrder > deck.ZOrder
            )
            .Where(s => R.Kind(s) == VisualComponentBase.VisualComponentType.Token)
            .OrderByDescending(s => s.ZOrder)
            .ToList();

    /// <summary>
    /// The contents of a component like a deck or bag.
    /// </summary>
    public static IEnumerable<ComponentState> Contents(this IRecordReader R, ComponentState s)
    {
        IEnumerable<ComponentState> inside = R.Kind(s) switch
        {
            VisualComponentBase.VisualComponentType.Deck => R.TokensOn(s),
            VisualComponentBase.VisualComponentType.Bag => R.Get<ComponentState>(c =>
                c.ContainerRef == s.Id
            ),
            _ => [],
        };
        foreach (var c in inside)
        {
            yield return c;
            foreach (var d in R.Contents(c))
                yield return d;
        }
    }
}
