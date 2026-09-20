using System.Collections.Generic;

/// <summary>
/// A single dataset row.
/// </summary>
public class DataRow : IReplicated
{
    public SnowTag Id { get; set; }

    /// <summary>Reversible soft-delete flag.</summary>
    public bool Deleted { get; set; }

    /// <summary>The id of the last event that updated this row.</summary>
    public SnowportId LastUpdateId { get; set; }

    /// <summary>The dataset this row belongs to.</summary>
    public SnowTag DataSetId { get; set; }

    /// <summary>LexoRank-style ordering key. See <see cref="RowRank"/>.</summary>
    public string Rank { get; set; } = string.Empty;

    /// <summary>Cell values keyed by <see cref="Column.Id"/>.</summary>
    public Dictionary<SnowTag, string> Data { get; set; } = new();
}
