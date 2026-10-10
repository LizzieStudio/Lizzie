using Godot;

/// <summary>
/// The flat plane the cursor moves on, at this node's height.
/// </summary>
public partial class DragPlane : Node3D
{
    public Vector3? GetCursorProjection() => Project(GetViewport().GetMousePosition());

    /// <summary>
    /// The 3D point on the plane under <paramref name="point"/>, or null when the point is at or above the horizon.
    /// </summary>
    public Vector3? Project(Vector2 point)
    {
        var camera = GetViewport().GetCamera3D();
        return new Plane(Vector3.Up, GlobalPosition.Y).IntersectsRay(
            camera.ProjectRayOrigin(point),
            camera.ProjectRayNormal(point)
        );
    }
}
