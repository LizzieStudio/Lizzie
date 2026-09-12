/// <summary>
/// Replicated in multiplayer on every update.
/// </summary>
public interface IReplicated
{
    SnowTag Id { get; set; }

    /// <summary>The id of the event that last wrote this definition.</summary>
    SnowportId LastUpdateId { get; set; }

    /// <summary>Reversible soft-delete flag.</summary>
    bool Deleted { get; set; }
}
