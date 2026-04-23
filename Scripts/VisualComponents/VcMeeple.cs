using System;
using System.Collections.Generic;
using Godot;

public partial class VcMeeple : VisualComponentBase
{
    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Meeple;

        MainMesh = GetNode<GeometryInstance3D>("MeshAnchor");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
    }

    public override bool Setup(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        base.Setup(parameters, textureFactory);

        MainMesh = GetNode<GeometryInstance3D>("MeshAnchor");

        foreach (var child in MainMesh.GetChildren())
        {
            child.QueueFree();
        }

        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        var h = Utility.GetParam<float>(parameters, "Height") / 10;
        var t = Utility.GetParam<float>(parameters, "Thickness") / 10;
        var c = Utility.GetParam<Color>(parameters, "Color");
        var g = Utility.GetParam<bool[][]>(parameters, "Grid");

        Height = h;

        _bounds.Clear();
        var r = new RectangleShape2D();
        r.Size = new Vector2(h, t);

        _bounds.Add(new OffsetShape2D(r));

        int rows = g.Length;
        int cols = g.Length > 0 ? g[0].Length : 0;

        float midx = rows / 2f;
        float midz = cols / 2f;

        var cubeHeight = h / rows;

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                if (g[i][j])
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

                    var v = new RectangleShape2D();
                    v.Size = new Vector2(cubeHeight, cubeHeight);
                    _voxels.Add(
                        new OffsetShape2D(
                            v,
                            new Vector2((i - midx) * cubeHeight, (j - midz) * cubeHeight)
                        )
                    );
                }
            }
        }

        HighlightMesh.Scale = new Vector3(h, t, h);

        YHeight = t;

        SetColor(c);

        return true;
    }

    #region Shape Profiles

    private enum MeepleOrientation
    {
        FlatUp,
        FlatDown,
        StandUp,
        UpsideDown,
        RightSide,
        LeftSide,
    }

    private MeepleOrientation _orientation = MeepleOrientation.FlatUp;

    // The profile changes based on whether the meeple is standing up or laying down
    private List<OffsetShape2D> _voxels = new();
    private List<OffsetShape2D> _bounds = new();

    public override List<OffsetShape2D> ShapeProfiles
    {
        get
        {
            switch (_orientation)
            {
                case MeepleOrientation.FlatUp:
                case MeepleOrientation.FlatDown:
                    return _voxels;
                case MeepleOrientation.StandUp:
                case MeepleOrientation.UpsideDown:
                case MeepleOrientation.LeftSide:
                case MeepleOrientation.RightSide:
                    return _bounds;
                default:
                    return new List<OffsetShape2D>();
            }
        }
        set { }
    }

    #endregion


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
        boxMesh.Size = new Vector3(s, t, s);

        // Assign the mesh to the instance
        meshInstance.Mesh = boxMesh;

        // Create a StandardMaterial3D for the color
        var material = new StandardMaterial3D();
        material.AlbedoColor = c;

        // Apply the material to the mesh
        meshInstance.SetSurfaceOverrideMaterial(0, material);

        // Set the position (x, s/2, y) - s/2 for height so cube sits on the ground
        //meshInstance.Position = new Vector3(x, s / 2f, z);
        meshInstance.Position = new Vector3(y, 0, x);

        return meshInstance;
    }
}
