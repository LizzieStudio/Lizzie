using System;
using System.Collections.Generic;
using Godot;

public partial class VcDisc : VisualComponentBase
{
    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Disc;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
    }

    public override bool Build(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        base.Build(parameters, textureFactory);

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        if (parameters.ContainsKey(nameof(Height)))
        {
            if (parameters[nameof(Height)] is float h)
            {
                if (h <= 0)
                    return false;
                Height = h / 10f;
            }

            if (parameters[nameof(Diameter)] is float w)
            {
                Diameter = w / 10f;
            }

            if (parameters["Color"] is Color color)
            {
                DiscColor = color;
            }
        }

        //create cylinder
        Scale = new Vector3(Diameter, Height, Diameter);

        YHeight = Height;
        SetColor(DiscColor);

        var c = new CircleShape2D();
        c.Radius = Diameter / 2f;
        ShapeProfiles.Add(new OffsetShape2D(c));

        return true;
    }

    public override List<string> ValidateParameters(Dictionary<string, object> parameters)
    {
        var ret = new List<string>();

        //must have a name and height. Width/length optional
        if (parameters.ContainsKey(nameof(ComponentName)))
        {
            if (string.IsNullOrEmpty(parameters[nameof(ComponentName)].ToString()))
                ret.Add("Instance Name may not be blank");
        }
        else
        {
            ret.Add("Instance Name not included");
        }

        if (parameters.ContainsKey(nameof(Height)))
        {
            if (parameters[nameof(Height)] is float h)
            {
                if (h <= 0)
                    ret.Add("Height must be > 0");
            }
        }
        else
        {
            ret.Add("Height not included");
        }

        if (parameters.ContainsKey(nameof(Diameter)))
        {
            if (parameters[nameof(Diameter)] is float d)
            {
                if (d <= 0)
                    ret.Add("Diameter must be > 0");
            }
        }
        else
        {
            ret.Add("Diameter not included");
        }

        return ret;
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(Height, Diameter);

    private float Height;
    private float Diameter;
    private Color DiscColor;
}
