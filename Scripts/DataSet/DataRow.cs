using System.Collections.Immutable;

/// <summary>
/// A single dataset row.
/// </summary>
public record DataRow : IReplicated
{
    public SnowTag Id { get; init; }

    /// <summary>Reversible soft-delete flag.</summary>
    public bool Deleted { get; init; }

    /// <summary>The id of the last event that updated this row.</summary>
    public SnowportId LastUpdateId { get; init; }

    /// <summary>The dataset this row belongs to.</summary>
    public SnowTag DataSetId { get; init; }

    /// <summary>LexoRank-style ordering key. See <see cref="RowRank"/>.</summary>
    public string Rank { get; init; } = string.Empty;

    /// <summary>Cell values keyed by <see cref="Column.Id"/>.</summary>
    public ImmutableDictionary<SnowTag, string> Data { get; init; } =
        ImmutableDictionary<SnowTag, string>.Empty;

    public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
        this with { Id = id, LastUpdateId = lastUpdateId };
}
