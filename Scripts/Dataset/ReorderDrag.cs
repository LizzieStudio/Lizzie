using Godot;

namespace Lizzie;

/// <summary>
/// A dataset editor row or column being dragged to a new place, as Godot drag-and-drop data.
/// </summary>
public sealed partial class ReorderDrag : RefCounted
{
    /// <summary>Rows move along Y, and columns along X.</summary>
    public Vector2.Axis Axis { get; init; }

    /// <summary>The row or column being moved.</summary>
    public SnowTag MovedRef { get; init; }
}
