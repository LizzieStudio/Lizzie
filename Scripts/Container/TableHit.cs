namespace Lizzie;

/// <summary>
/// What's on the table under a 2D, screen-space point in the viewport.
/// </summary>
public readonly record struct TableHit
{
    /// <summary>
    /// The component there, or null.
    /// </summary>
    public VisualComponentBase Component { get; init; }

    /// <summary>
    /// Whether it's over a container's draw area, so a drag draws from it.
    /// </summary>
    public bool DrawArea { get; init; }
}
