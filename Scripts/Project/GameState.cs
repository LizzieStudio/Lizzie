using System.Collections.Immutable;

public record GameState : Replicated
{
    /// <summary>The linked snapshot or <see cref="SnowTag.Empty"/>.</summary>
    public SnowTag Parent { get; init; }

    /// <summary>Human-readable name chosen by the user.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>A freeform description entered by the user (optional).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// A delta vs the parent:
    /// * one upsert for each added component
    /// * one upsert for each transformed component
    /// * one delete upsert for each removed component
    /// </summary>
    public ImmutableArray<ComponentEffect> Upserts { get; init; } =
        ImmutableArray<ComponentEffect>.Empty;
}
