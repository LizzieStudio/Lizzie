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
        // the SnowTags in kept atoms, and Empty for no stage
        var found = new HashSet<SnowTag> { SnowTag.Empty };
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
                    if (p.Removed || !found.Contains(p.StageRef.Tag))
                        continue;
                    if (!placed.Contains(p.ContainerRef.Tag))
                        continue;
                }
                else if (!keys.Exists(k => k.IsSubsetOf(found)))
                    continue;
                kept.Add(atom);
                JsonWalker.Visit<SnowTag>(atom, tag => found.Add(tag));
                if (atom is ComponentPosition whole)
                    placed.Add(whole.Id.Tag);
                waiting.RemoveAt(i);
                grew = true;
            }
        }

        kept.AddRange(
            waiting
                .Select(w => w.Atom)
                .OfType<ComponentPosition>()
                .Where(p => found.Contains(p.StageRef.Tag) && placed.Contains(p.Id.Tag))
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
    /// </summary>
    public static string Serialize(TimelineEvent save)
    {
        var found = new SortedSet<SnowTag>();
        JsonWalker.Visit<SnowTag>(save, tag => found.Add(tag));
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
}
