using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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

    private bool _defaultIncluded;
    private bool _hiddenWhenExcluded;
    private HashSet<int> _includedSeats = new();
    private HashSet<int> _excludedSeats = new();

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
        ComponentParameters parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        base.Setup(parameters, dataSetRow, textureFactory);
        var p = (ZoneParameters)parameters;

        MainMesh = GetNodeOrNull<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNodeOrNull<MeshInstance3D>("HighlightMesh");

        _width = p.Width;
        _depth = p.Depth;

        _defaultIncluded = p.DefaultIncluded;
        _hiddenWhenExcluded = p.HiddenWhenExcluded;
        _includedSeats = new HashSet<int>(p.IncludedSeats);
        _excludedSeats = new HashSet<int>(p.ExcludedSeats);

        if (MainMesh != null)
            MainMesh.Scale = new Vector3(_width, 1f, _depth);

        PositionHandle();

        return true;
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

    public bool DefaultIncluded => _defaultIncluded;
    public bool HiddenWhenExcluded => _hiddenWhenExcluded;
    public HashSet<int> IncludedSeats => new(_includedSeats);
    public HashSet<int> ExcludedSeats => new(_excludedSeats);

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
}

public sealed class ZoneParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Zone;

    public float Width { get; set; } = 2f;
    public float Depth { get; set; } = 2f;
    public bool DefaultIncluded { get; set; }
    public bool HiddenWhenExcluded { get; set; }
    public List<int> IncludedSeats { get; set; } = new();
    public List<int> ExcludedSeats { get; set; } = new();
}
