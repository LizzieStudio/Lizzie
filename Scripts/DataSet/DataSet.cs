using System.Collections.Immutable;

public record DataSet : IReplicated
{
    public SnowTag Id { get; init; }

    /// <summary>
    /// Reversible soft-delete flag.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>The id of the last event that updated this dataset.</summary>
    public SnowportId LastUpdateId { get; init; }

    public string Name { get; init; }

    /// <summary>
    /// The dataset's columns, in display order.
    /// </summary>
    public ImmutableArray<Column> Columns { get; init; } = ImmutableArray<Column>.Empty;

    public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
        this with
        {
            Id = id,
            LastUpdateId = lastUpdateId,
        };
}

public record Column
{
    public SnowTag Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
