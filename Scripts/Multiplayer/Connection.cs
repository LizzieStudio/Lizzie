/// <summary>
/// Session data for a connected client.
/// </summary>
public class Connection : IReplicated
{
    /// <summary>The player's identity. Uses the same source byte as the client</summary>
    public SnowTag Id { get; set; }

    /// <summary>The id of the event that last wrote this record.</summary>
    public SnowportId LastUpdateId { get; set; }

    /// <summary>Soft-delete flag.</summary>
    public bool Deleted { get; set; }

    /// <summary>The seat this player claimed.</summary>
    public int Seat { get; set; }

    /// <summary>The container id for this player's cursor.</summary>
    public SnowTag CursorRef { get; set; }
}
