using System.Collections.Immutable;

public record DataSet : Replicated
{
    public string Name { get; init; }

    /// <summary>
    /// The dataset's columns, in display order.
    /// </summary>
    public ImmutableArray<Column> Columns { get; init; } = ImmutableArray<Column>.Empty;
}

public record Column
{
    public SnowTag Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
