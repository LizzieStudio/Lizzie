using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Lizzie.Replication.Machinery;

/// <summary>
/// Makes a save as small as it can be.
/// <list type="bullet">
/// <item><see cref="Collect"/> drops deleted records that nothing refers to, like garbage collection.</item>
/// <item><see cref="Serialize"/> renumbers every id from 1 on source number 0 (admin).</item>
/// </list>
/// </summary>
public static class SaveCompaction
{
    /// <summary>
    /// The records that need to be kept.
    /// </summary>
    public static IReadOnlyList<Replicated> Collect(IEnumerable<Replicated> records)
    {
        var all = records.ToList();
        var byId = all.ToLookup(r => r.Id);

        var kept = new HashSet<Replicated>(
            all.Where(r => !r.Deleted),
            ReferenceEqualityComparer.Instance
        );
        var unvisited = new Queue<Replicated>(kept);
        while (unvisited.TryDequeue(out var record))
            JsonWalker.Visit<SnowTag>(
                record,
                tag =>
                {
                    foreach (var referenced in byId[tag])
                        if (kept.Add(referenced))
                            unvisited.Enqueue(referenced);
                },
                skipRecordIds: true
            );

        return all.Where(kept.Contains).ToList();
    }

    /// <summary>
    /// The event as one line of JSON, with its SnowTags and SnowportIds renumbered from 1 under the admin source.
    /// The numbers keep their order, and equal ids stay equal.
    /// </summary>
    public static string Serialize(TableEvent save)
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

    private static Dictionary<T, T> Renumber<T>(TableEvent save, T empty, Func<int, T> number)
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
