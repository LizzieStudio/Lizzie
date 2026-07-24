using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A zone is a draggable rectangular component on the table that governs which players may
/// <b>see</b> and/or <b>move</b> the components geometrically inside it.
/// <see cref="ZoneService"/> reads them to resolve each contained component's visibility/control.
/// </summary>
public partial class VcZone : VisualComponentBase
{
    public const string WidthKey = "Width";
    public const string DepthKey = "Depth";
    public const string DefaultIncludedKey = "DefaultIncluded";
    public const string IncludedSeatsKey = "IncludedSeats";
    public const string ExcludedSeatsKey = "ExcludedSeats";
    public const string HiddenWhenExcludedKey = "HiddenWhenExcluded";

    private float _width = 2f;
    private float _depth = 2f;

    private const float HandleLift = 0.08f;

    private Node3D _handleMesh;
    private Node3D _handleCollision;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Zone;

        MainMesh = GetNodeOrNull<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNodeOrNull<MeshInstance3D>("HighlightMesh");
        _handleMesh = GetNodeOrNull<Node3D>("HandleMesh");
        _handleCollision = GetNodeOrNull<Node3D>("CollisionShape3D");

        // A zone always sits beneath everything.
        NeverHighlight = true;
    }

    private void PositionHandle()
    {
        _handleMesh ??= GetNodeOrNull<Node3D>("HandleMesh");
        _handleCollision ??= GetNodeOrNull<Node3D>("CollisionShape3D");

        var corner = new Vector3(-_width / 2f, HandleLift, -_depth / 2f);
        if (_handleMesh != null)
            _handleMesh.Position = corner;
        if (_handleCollision != null)
            _handleCollision.Position = corner;
    }

    public override bool Setup(
        Dictionary<string, object> parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        return Setup(parameters, textureFactory);
    }

    public override bool Setup(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        base.Setup(parameters, string.Empty, textureFactory);

        MainMesh = GetNodeOrNull<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNodeOrNull<MeshInstance3D>("HighlightMesh");

        _width = JsonUtilities.TryGetFloat(parameters, WidthKey, _width);
        _depth = JsonUtilities.TryGetFloat(parameters, DepthKey, _depth);

        if (MainMesh != null)
            MainMesh.Scale = new Vector3(_width, 1f, _depth);

        PositionHandle();

        return true;
    }

    public override List<string> ValidateParameters(Dictionary<string, object> parameters)
    {
        var ret = new List<string>();

        if (parameters.ContainsKey(nameof(ComponentName)))
        {
            if (string.IsNullOrEmpty(parameters[nameof(ComponentName)].ToString()))
                ret.Add("Instance Name may not be blank");
        }
        else
        {
            ret.Add("Instance Name not included");
        }

        if (JsonUtilities.TryGetFloat(parameters, WidthKey) <= 0f)
            ret.Add("Width must be > 0");
        if (JsonUtilities.TryGetFloat(parameters, DepthKey) <= 0f)
            ret.Add("Depth must be > 0");

        return ret;
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(_width, _depth);

    //Zones are always ZOrder -1
    public override int ZOrder
    {
        get => -1;
        set { }
    }

    /// <summary>
    /// True if the given world position falls within this zone's footprint on the XZ plane.
    /// </summary>
    public bool Contains(Vector3 worldPosition)
    {
        var local = ToLocal(worldPosition);
        return Mathf.Abs(local.X) <= _width / 2f && Mathf.Abs(local.Z) <= _depth / 2f;
    }

    public bool DefaultIncluded => JsonUtilities.TryGetBool(Parameters, DefaultIncludedKey);
    public bool HiddenWhenExcluded => JsonUtilities.TryGetBool(Parameters, HiddenWhenExcludedKey);
    public HashSet<int> IncludedSeats => SeatSetFor(Parameters, IncludedSeatsKey);
    public HashSet<int> ExcludedSeats => SeatSetFor(Parameters, ExcludedSeatsKey);

    /// <summary>
    /// Whether the given seat is considered "included" by this zone's rules.
    /// </summary>
    public bool SeatIncluded(int seatIndex)
    {
        if (ExcludedSeats.Contains(seatIndex))
            return false;
        if (IncludedSeats.Contains(seatIndex))
            return true;
        return DefaultIncluded;
    }

    public static HashSet<int> SeatSetFor(Dictionary<string, object> p, string key) =>
        JsonUtilities.TryGetIntSet(p, key);
}
