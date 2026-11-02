using System;
using System.Collections;
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
/// <item><see cref="Serialize"/> renumbers every SnowTag from 1 on source number 0 (admin).</item>
/// </list>
/// </summary>
public static class SaveCompaction
{
    /// <summary>
    /// Gets one compacted admin event for the save.
    /// </summary>
    public static TimelineEvent Event(IEnumerable<AtomEntry> saved)
    {
        var entries = saved.ToList();
        return new()
        {
            Id = new SnowStamp(entries.Max(e => e.LastUpdate).LogicClock, Source.Admin),
            Writes = Collect(entries.Select(e => e.Value)),
        };
    }

    /// <summary>
    /// Gets the atoms that need to be kept, the ones reachable through their queries, in a fixed order.
    ///
    /// <para>
    /// An atom is kept when its id, or one of its lookup keys, holds no SnowTags but those found in kept atoms.
    /// An <see cref="All{T}"/> key holds no SnowTags, so it keeps every atom it lists,
    /// and a kept atom's own id reaches the atoms grouped under it, like a dataset's rows.
    /// An atom no query lists, like a deleted <see cref="Prototype"/>, stays only while a kept atom refers to it.
    /// A <see cref="WeakMap{TKey, TValue}"/>'s keys keep nothing, but once a key is reached its value counts.
    /// </para>
    ///
    /// <para>
    /// A <see cref="ComponentPosition"/> is kept only while its stage is kept.
    /// Then, unless it's a removal, it's kept whole when either:
    /// <list type="bullet">
    /// <item>It isn't in a container, like one on the table or in a hand.</item>
    /// <item>Its container has a position kept whole in some stage.</item>
    /// </list>
    /// Anything else, removals included, is kept as a bare removal while its own component has a position kept whole in some stage,
    /// since it may be hiding a blueprint's copy of the component. Otherwise it's dropped.
    /// </para>
    /// </summary>
    public static Atom[] Collect(IEnumerable<Atom> atoms)
    {
        var waiting = atoms
            .Select(a => (Atom: a, Keys: a is ComponentPosition ? [] : Keys(a)))
            .ToList();
        var reach = new Reach();
        // the components with a position kept whole, and Empty for the table
        var placed = new HashSet<SnowTag> { SnowTag.Empty };
        var kept = new List<Atom>();

        // Each pass keeps what the last one's atoms reached.
        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                var (atom, keys) = waiting[i];
                if (atom is ComponentPosition p)
                {
                    if (p.Removed || !reach.Tags.Contains(p.StageRef.Tag))
                        continue;
                    if (!placed.Contains(p.ContainerRef.Tag))
                        continue;
                }
                else if (!keys.Exists(k => k.IsSubsetOf(reach.Tags)))
                    continue;
                kept.Add(atom);
                reach.Visit(atom);
                if (atom is ComponentPosition whole)
                    placed.Add(whole.Id.Tag);
                waiting.RemoveAt(i);
                grew = true;
            }
            if (reach.Grow())
                grew = true;
        }

        kept.AddRange(
            waiting
                .Select(w => w.Atom)
                .OfType<ComponentPosition>()
                .Where(p => reach.Tags.Contains(p.StageRef.Tag) && placed.Contains(p.Id.Tag))
                .Select(p => p.Removal())
        );

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
    /// The event as one line of JSON, with its SnowTags renumbered from 1 under the admin source.
    /// The numbers keep their order, and equal tags stay equal.
    /// Only the tags the save reaches get numbers,
    /// so a <see cref="WeakMap{TKey, TValue}"/> entry isn't saved unless something else keeps its key.
    /// </summary>
    public static string Serialize(TimelineEvent save)
    {
        var reach = new Reach();
        reach.Visit(save);
        reach.Grow();
        var found = new SortedSet<SnowTag>(reach.Tags);
        found.Remove(SnowTag.Empty);

        var tags = new Dictionary<SnowTag, SnowTag>(found.Count);
        foreach (var tag in found)
            tags[tag] =
                tags.Count < SnowTag.MaxCounter
                    ? new SnowTag(Source.Admin, tags.Count + 1)
                    : throw new InvalidOperationException(
                        $"A save can't hold more than {SnowTag.MaxCounter} SnowTags."
                    );
        return JsonSerializer.Serialize(save, LizzieJson.SaveOptions(tags));
    }

    /// <summary>
    /// The SnowTags reached from what's been visited.
    /// A <see cref="WeakMap{TKey, TValue}"/>'s entry is reached only once its key is.
    /// </summary>
    private sealed class Reach
    {
        // Empty stands for no stage, and for keys that hold no SnowTags.
        public HashSet<SnowTag> Tags { get; } = [SnowTag.Empty];

        // the entries of visited WeakMaps whose keys haven't been reached
        private readonly List<DictionaryEntry> _waiting = [];

        public void Visit(object value) =>
            JsonWalker.Visit<SnowTag>(
                value,
                tag => Tags.Add(tag),
                map =>
                {
                    foreach (DictionaryEntry entry in map.Entries)
                        _waiting.Add(entry);
                }
            );

        /// <summary>
        /// Visits the value of every waiting entry whose key is reached, including those its values reach.
        /// </summary>
        /// <returns>Whether any entry was reached.</returns>
        public bool Grow()
        {
            bool any = false;
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = _waiting.Count - 1; i >= 0; i--)
                {
                    var entry = _waiting[i];
                    bool reached = true;
                    JsonWalker.Visit<SnowTag>(entry.Key, tag => reached &= Tags.Contains(tag));
                    if (!reached)
                        continue;
                    _waiting.RemoveAt(i);
                    Visit(entry.Value);
                    grew = any = true;
                }
            }
            return any;
        }
    }
}
