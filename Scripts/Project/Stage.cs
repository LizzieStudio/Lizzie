/// <summary>
/// A blueprint or a game being played.
/// </summary>
[JsonName("Stage")]
public sealed record Stage : Atom
{
    public static readonly Id<Stage, SnowTag> ById = new(s => s.Id);

    /// <summary>
    /// Blueprints that are not deleted.
    /// </summary>
    public static readonly All<Stage> Live = new(s => !s.Deleted && !s.IsGame);

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
    /// Whether this is a game rather than a blueprint.
    /// </summary>
    public bool IsGame { get; init; }

    public bool Deleted { get; init; }
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
