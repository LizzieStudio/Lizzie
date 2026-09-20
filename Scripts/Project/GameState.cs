using System.Collections.Immutable;

public record GameState : IReplicated
{
    /// <summary>Unique identity of this saved state.</summary>
    public SnowTag Id { get; init; }

    /// <summary>The linked snapshot or <see cref="SnowTag.Empty"/>.</summary>
    public SnowTag Parent { get; init; }

    /// <summary>Reversible soft-delete flag.</summary>
    public bool Deleted { get; init; }

    /// <summary>The id of the last event that wrote this record.</summary>
    public SnowportId LastUpdateId { get; init; }

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

    public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
        this with
        {
            Id = id,
            LastUpdateId = lastUpdateId,
        };
}
