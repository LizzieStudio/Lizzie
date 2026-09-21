using System.Collections.Immutable;

/// <summary>
/// A single dataset row.
/// </summary>
public record DataRow : Replicated
{
    /// <summary>The dataset this row belongs to.</summary>
    public SnowTag DataSetId { get; init; }

    /// <summary>LexoRank-style ordering key. See <see cref="RowRank"/>.</summary>
    public string Rank { get; init; } = string.Empty;

    /// <summary>Cell values keyed by <see cref="Column.Id"/>.</summary>
    public ImmutableDictionary<SnowTag, string> Data { get; init; } =
        ImmutableDictionary<SnowTag, string>.Empty;
}
