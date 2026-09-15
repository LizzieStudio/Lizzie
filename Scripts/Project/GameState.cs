using System;

public class GameState : IReplicated
{
    public GameState() { }

    /// <summary>Unique identity of this saved state.</summary>
    public SnowTag Id { get; set; }

    /// <summary>The linked snapshot or <see cref="SnowTag.Empty"/>.</summary>
    public SnowTag Parent { get; set; }

    /// <summary>Reversible soft-delete flag.</summary>
    public bool Deleted { get; set; }

    /// <summary>The id of the last event that wrote this record.</summary>
    public SnowportId LastUpdateId { get; set; }

    /// <summary>Human-readable name chosen by the user.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A freeform description entered by the user (optional).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// A delta vs the parent:
    /// * one upsert for each added component
    /// * one upsert for each transformed component
    /// * one delete upsert for each removed component
    /// </summary>
    public ComponentEffect[] Upserts { get; set; } = Array.Empty<ComponentEffect>();
}
