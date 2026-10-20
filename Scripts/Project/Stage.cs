using System;
using System.Collections.Generic;

/// <summary>
/// A blueprint or a game being played.
/// </summary>
[JsonName("Stage")]
public sealed record Stage : Atom, IRanked
{
    public static readonly Id<Stage, SnowTag> ById = new(s => s.Id);

    /// <summary>
    /// Blueprints that are not deleted, by the stage they build on, with <see cref="SnowTag.Empty"/> for the roots.
    /// </summary>
    public static readonly Group<Stage, SnowTag?> Children = new(s =>
        s.Deleted || s.IsGame ? null : s.ParentRef
    );

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    public string Name { get; init; }

    /// <summary>
    /// The stage this one builds on, or <see cref="SnowTag.Empty"/> for a root blueprint.
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
    /// Whether this is a game rather than a blueprint.
    /// </summary>
    public bool IsGame { get; init; }

    public bool Deleted { get; init; }

    /// <summary>
    /// Gets the changes that start a new game on <paramref name="blueprint"/>.
    /// </summary>
    public static IChange[] NewGame(IAtomReader R, SnowTag blueprint)
    {
        var current = R.Get(CurrentGame.Only);
        var game = new Stage
        {
            Id = Snowport.Clock.CreateTag(),
            Name = "Game",
            ParentRef = blueprint,
            IsGame = true,
        };
        IChange[] started =
        [
            game,
            current with
            {
                StageRef = game.Id,
            },
            R.Get(ProjectGameSettings.Only) with
            {
                BlueprintRef = blueprint,
            },
        ];
        return R.Get(ById, current.StageRef) is { } old ? [R.Unset(old), .. started] : started;
    }

    /// <summary>
    /// Gets whether or not the user should be allowed to delete <paramref name="blueprint"/>.
    /// </summary>
    public static bool IsLeaf(IAtomReader R, Stage blueprint) =>
        R.Count(Children, blueprint.Id) == 0
        && R.Get(ById, R.Get(CurrentGame.Only).StageRef)?.ParentRef != blueprint.Id;

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
/// The game being played.
/// </summary>
[NotSaved]
[JsonName("CurrentGame")]
public sealed record CurrentGame : Atom
{
    public static readonly Singleton<CurrentGame> Only = new();

    /// <summary>
    /// The game's <see cref="Stage"/>.
    /// </summary>
    public SnowTag StageRef { get; init; }
}
