using System.Collections.Generic;

public class DataSet : IReplicated
{
    public SnowTag Id { get; set; }

    /// <summary>
    /// Reversible soft-delete flag.
    /// </summary>
    public bool Deleted { get; set; }

    /// <summary>The id of the last event that updated this dataset.</summary>
    public SnowportId LastUpdateId { get; set; }

    public string Name { get; set; }

    /// <summary>
    /// The dataset's columns, in display order.
    /// </summary>
    public List<Column> Columns { get; set; } = new();
}

public class Column
{
    public SnowTag Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
