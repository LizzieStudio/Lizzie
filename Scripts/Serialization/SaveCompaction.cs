using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// Makes a save as small as it can be.
/// <list type="bullet">
/// <item><see cref="Collect"/> drops the atoms nothing reaches, like garbage collection.</item>
/// <item><see cref="Serialize"/> renumbers every id from 1 on source number 0 (admin).</item>
/// </list>
/// </summary>
public static class SaveCompaction
{
    /// <summary>
    /// Gets the atoms that need to be kept, the ones reachable through their queries, in a fixed order.
    ///
    /// <para>
    /// An atom is kept when its id, or one of its lookup keys, holds no SnowTags but those found in kept atoms.
    /// An <see cref="All{T}"/> key holds no SnowTags, so it keeps every atom it lists,
    /// and a kept atom's own id reaches the atoms grouped under it, like a dataset's rows.
    /// An atom no query lists, like a deleted <see cref="Prototype"/>, stays only while a kept atom refers to it.
    /// </para>
    /// </summary>
    public static Atom[] Collect(IEnumerable<Atom> atoms)
    {
        var waiting = atoms.Select(a => (Atom: a, Keys: Keys(a))).ToList();
        var found = new HashSet<SnowTag>();
        var kept = new List<Atom>();

        // Each pass keeps what the last one's atoms reached.
        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                var (atom, keys) = waiting[i];
                if (!keys.Exists(k => k.IsSubsetOf(found)))
                    continue;
                kept.Add(atom);
                JsonWalker.Visit<SnowTag>(atom, tag => found.Add(tag));
                waiting.RemoveAt(i);
                grew = true;
            }
        }

        return kept.OrderBy(a => a.GetType().Name, StringComparer.Ordinal)
            .ThenBy(
                a =>
                    JsonSerializer.Serialize(
                        IdQuery.For(a.GetType()).Of(a),
                        LizzieJson.EventOptions
                    ),
                NumericOrder
            )
            .ToArray();
    }

    private static readonly StringComparer NumericOrder = StringComparer.Create(
        CultureInfo.InvariantCulture,
        CompareOptions.NumericOrdering
    );

    /// <summary>
    /// The SnowTags in each id and lookup key that could keep <paramref name="atom"/>.
    /// </summary>
    private static List<HashSet<SnowTag>> Keys(Atom atom)
    {
        var keys = new List<HashSet<SnowTag>>();
        foreach (var query in Query.DeclaredOn(atom.GetType()))
        {
            if (query.Of(atom) is not { } key)
                continue;
            var tags = new HashSet<SnowTag>();
            JsonWalker.Visit<SnowTag>(key, tag => tags.Add(tag));
            tags.Remove(SnowTag.Empty);
            keys.Add(tags);
        }
        return keys;
    }

    /// <summary>
    /// The event as one line of JSON, with its SnowTags and SnowportIds renumbered from 1 under the admin source.
    /// The numbers keep their order, and equal ids stay equal.
    /// </summary>
    public static string Serialize(TimelineEvent save)
    {
        var tags = Renumber(save, SnowTag.Empty, Tag);
        var ids = Renumber(save, SnowportId.Empty, Id);
        return JsonSerializer.Serialize(save, LizzieJson.SaveOptions(tags, ids));
    }

    private static SnowTag Tag(int n) =>
        n <= SnowTag.MaxCounter
            ? new SnowTag(Snowport.AdminSource, n)
            : throw new InvalidOperationException(
                $"A save can't hold more than {SnowTag.MaxCounter} SnowTags."
            );

    private static SnowportId Id(int n) => new(n, Snowport.AdminSource);

    private static Dictionary<T, T> Renumber<T>(TimelineEvent save, T empty, Func<int, T> number)
        where T : IComparable<T>
    {
        var found = new SortedSet<T>();
        JsonWalker.Visit<T>(save, value => found.Add(value));
        found.Remove(empty);

        var map = new Dictionary<T, T>(found.Count);
        foreach (var value in found)
            map[value] = number(map.Count + 1);
        return map;
    }
}
