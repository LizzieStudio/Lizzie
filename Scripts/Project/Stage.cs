using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie;

/// <summary>
/// A blueprint or a table, which is played on.
/// </summary>
[JsonName("Stage")]
public sealed record Stage : Atom, IRanked
{
    public static readonly Identity<Stage, SnowTag> ById = new(s => s.Id);

    /// <summary>
    /// Stages that are not deleted, by the stage they build on, with <see cref="SnowTag.Empty"/> for the roots.
    /// </summary>
    public static readonly Lookup<Stage, SnowTag?> Children = new(s =>
        s.Deleted ? null : s.ParentRef
    );

    /// <summary>
    /// This atom's identity, created with <see cref="SnowTag.Create"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    public string Name { get; init; }

    /// <summary>
    /// The stage this one builds on, or <see cref="SnowTag.Empty"/> for a root.
    /// </summary>
    public SnowTag ParentRef { get; init; }

    /// <summary>
    /// Its position among its siblings.
    /// </summary>
    /// <remarks>
    /// This is purely aesthetics.
    /// It is only used for the blueprint tree in the blueprint manager.
    /// </remarks>
    public SnowRank Rank { get; init; }

    /// <summary>
    /// Whether this is a table rather than a blueprint.
    /// </summary>
    public bool IsTable { get; init; }

    public bool Deleted { get; init; }

    /// <summary>
    /// Gets a new empty table, last under <paramref name="parent"/>.
    /// </summary>
    public static Stage NewTable(IAtomReader R, SnowTag parent) =>
        new()
        {
            Id = SnowTag.Create(),
            Name = NextTableName(R),
            ParentRef = parent,
            Rank = GetChildren(R, parent)
                .Select(s => s.Rank)
                .DefaultIfEmpty()
                .Max()
                .After(SnowportId.Create()),
            IsTable = true,
        };

    /// <summary>
    /// "Table N", numbered one past the highest "Table N" anywhere in the tree.
    /// </summary>
    public static string NextTableName(IAtomReader R)
    {
        int Highest(SnowTag parent) =>
            R.Get(Children, parent)
                .Select(s =>
                    Math.Max(
                        s.Name?.StartsWith("Table ") == true && int.TryParse(s.Name[6..], out int n)
                            ? n
                            : 0,
                        Highest(s.Id)
                    )
                )
                .DefaultIfEmpty()
                .Max();
        return $"Table {Highest(SnowTag.Empty) + 1}";
    }

    /// <summary>
    /// Gets whether the current table is <paramref name="id"/> or builds on it.
    /// </summary>
    public static bool IsCurrentTableOrAncestor(IAtomReader R, SnowTag id) =>
        Chain(R, R.Get(CurrentTable.Only).StageRef).Any(s => s.Id == id);

    /// <summary>
    /// The stages built on <paramref name="parent"/>, in the order the blueprint manager shows them.
    /// </summary>
    public static List<Stage> GetChildren(IAtomReader R, SnowTag parent) =>
        R.Get(Children, parent).OrderBy(s => s.Rank).ThenBy(s => s.Id).ToList();

    /// <summary>
    /// Gets what <paramref name="into"/> must write to merge in <paramref name="from"/>.
    /// The later stages take priority over earlier stages.
    /// </summary>
    public static IEnumerable<ComponentPosition> Union(
        IAtomReader R,
        SnowTag into,
        params SnowTag[] from
    ) =>
        from.SelectMany(s => R.Get(ComponentPosition.WrittenIn, s))
            .GroupBy(c => c.Id)
            .Select(g => g.Last())
            .Where(c => c.StageRef != into)
            .Select(c => c with { StageRef = into });

    /// <summary>
    /// Gets the changes that delete <paramref name="stage"/> and replace it in the tree with its children.
    /// </summary>
    public static IEnumerable<IChange> Dissolve(IAtomReader R, Stage stage)
    {
        var siblings = GetChildren(R, stage.ParentRef);
        int index = siblings.IndexOf(stage);
        siblings.RemoveAt(index);
        var changes = new List<IChange> { stage with { Deleted = true } };
        foreach (var child in GetChildren(R, stage.Id))
        {
            var moved = child with
            {
                ParentRef = stage.ParentRef,
                Rank = SnowRank.At(siblings, index, SnowportId.Create()),
            };
            siblings.Insert(index++, moved);
            changes.Add(moved);
        }
        return changes;
    }

    /// <summary>
    /// The stage of <paramref name="id"/> and the stages it builds on, nearest first,
    /// or nothing if it isn't <see cref="Reachable(IAtomReader, SnowTag)"/>.
    /// </summary>
    public static IEnumerable<Stage> Chain(IAtomReader R, SnowTag id)
    {
        if (!Reachable(R, id))
            yield break;
        for (var stage = R.Get(ById, id); stage != null; stage = R.Get(ById, stage.ParentRef))
            yield return stage;
    }

    /// <summary>
    /// Gets whether the stage <paramref name="id"/> is live,
    /// meaning neither it nor any of its ancestors have been removed.
    /// </summary>
    public static bool Reachable(IAtomReader R, SnowTag id) =>
        Reachable(id, s => R.Get(ById, s) is { Deleted: false } stage ? stage.ParentRef : null);

    /// <inheritdoc cref="Reachable(IAtomReader, SnowTag)"/>
    /// <param name="parentOf">A stage's parent, or null if it's missing or deleted.</param>
    public static bool Reachable(SnowTag id, Func<SnowTag, SnowTag?> parentOf)
    {
        var walked = new HashSet<SnowTag>();
        for (var stage = id; walked.Add(stage); )
        {
            if (parentOf(stage) is not { } parent)
                return false;
            if (parent == SnowTag.Empty)
                return true;
            stage = parent;
        }
        return false;
    }
}

/// <summary>
/// The table being played, which everyone sees unless they pick another stage.
/// </summary>
[JsonName("CurrentTable")]
public sealed record CurrentTable : Atom
{
    public static readonly Singleton<CurrentTable> Only = new();

    /// <summary>
    /// The table's <see cref="Stage"/>.
    /// </summary>
    public SnowTag StageRef { get; init; }
}
