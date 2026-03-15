using System;
using System.Collections.Generic;
using Godot;

public partial class VcMeeple : VisualComponentBase
{
    public override void _Ready()
    {
        base._Ready();
        Visible = true;
        ComponentType = VisualComponentType.Meeple;

        MainMesh = GetNode<GeometryInstance3D>("MeshAnchor");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
    }

    public override bool Build(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        base.Build(parameters, textureFactory);

        MainMesh = GetNode<GeometryInstance3D>("MeshAnchor");

        foreach (var child in MainMesh.GetChildren())
        {
            child.QueueFree();
        }

        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        var h = Utility.GetParam<float>(parameters, "Height") / 10;
        var t = Utility.GetParam<float>(parameters, "Thickness") / 10;
        var c = Utility.GetParam<Color>(parameters, "Color");
        var g = Utility.GetParam<bool[,]>(parameters, "Grid");

        Height = h;

        float midx = g.GetLength(0) / 2;
        float midz = g.GetLength(1) / 2;

        var cubeHeight = h / g.GetLength(0);

        for (int i = 0; i < g.GetLength(0); i++)
        {
            for (int j = 0; j < g.GetLength(1); j++)
            {
                if (g[i, j])
                {
                    MainMesh.AddChild(
                        CreateCubeMesh(
                            cubeHeight,
                            t,
                            c,
                            (i - midx) * cubeHeight,
                            (j - midz) * cubeHeight
                        )
                    );
                }
            }
        }

        HighlightMesh.Scale = new Vector3(h, h, t);

        YHeight = Height;

        SetColor(c);

        var r = new RectangleShape2D();
        r.Size = new Vector2(h, t);

        ShapeProfiles.Add(r);

        return true;
    }

    public override void SetColor(Color color)
    {
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = color;

        foreach (var c in MainMesh.GetChildren())
        {
            if (c is MeshInstance3D mesh)
            {
                mesh.MaterialOverride = mat;
            }
        }
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
            if (parameters[nameof(Height)] is int h)
            {
                if (h <= 0)
                    ret.Add("Height must be > 0");
            }
        }
        else
        {
            ret.Add("Height not included");
        }

        return ret;
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(Math.Max(Height, Width), Length);

    private float Height;
    private float Width;
    private float Length;
    private Color CubeColor;

    /// <summary>
    /// Creates a cube mesh object with specified parameters
    /// </summary>
    /// <param name="s">Side length of the cube</param>
    /// <param name="c">Color of the cube</param>
    /// <param name="x">X position</param>
    /// <param name="z">Y position (Z in 3D space)</param>
    /// <returns>MeshInstance3D of the created cube</returns>
    public MeshInstance3D CreateCubeMesh(float s, float t, Color c, float x, float y)
    {
        // Create a new MeshInstance3D
        var meshInstance = new MeshInstance3D();

        // Create a BoxMesh (cube)
        var boxMesh = new BoxMesh();
        boxMesh.Size = new Vector3(s, s, t);

        // Assign the mesh to the instance
        meshInstance.Mesh = boxMesh;

        // Create a StandardMaterial3D for the color
        var material = new StandardMaterial3D();
        material.AlbedoColor = c;

        // Apply the material to the mesh
        meshInstance.SetSurfaceOverrideMaterial(0, material);

        // Set the position (x, s/2, y) - s/2 for height so cube sits on the ground
        //meshInstance.Position = new Vector3(x, s / 2f, z);
        meshInstance.Position = new Vector3(y, -x, 0);

        return meshInstance;
    }
}
