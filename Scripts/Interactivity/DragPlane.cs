using Godot;
using System.Collections.Generic;

public partial class DragPlane : StaticBody3D
{
    public Vector3 GetCursorProjection()
    {
        var mousePosition = GetViewport().GetMousePosition();

        InputRayPickable = true;
        var from = GetViewport().GetCamera3D().ProjectRayOrigin(mousePosition);
        var to = from + GetViewport().GetCamera3D().ProjectRayNormal(mousePosition) * 500;
        var ray = new PhysicsRayQueryParameters3D
        {
            From = from,
            To = to,
            CollisionMask = 4
        };
        var res = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        InputRayPickable = false;

        return (Vector3)res.GetValueOrDefault("position", new Vector3(-99, -99, -99));
    }
}
