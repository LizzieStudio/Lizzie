using Godot;
using System;
using System.Collections.Generic;

public partial class VcZone : VisualComponentBase
{
    public override List<string> ValidateParameters(Dictionary<string, object> parameters)
    {
        throw new NotImplementedException();
    }

    public override float MaxAxisSize => 1;
    public override GeometryInstance3D DragMesh { get; }

    //Zones are always ZOrder -1
    public override int ZOrder
    {
        get => -1;
        set { }
    }
}
