using System;
using System.Collections.Generic;
using System.Text.Json;
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

        _width = ReadFloat(parameters, WidthKey, _width);
        _depth = ReadFloat(parameters, DepthKey, _depth);

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

        if (ReadFloat(parameters, WidthKey, 0f) <= 0f)
            ret.Add("Width must be > 0");
        if (ReadFloat(parameters, DepthKey, 0f) <= 0f)
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

    public bool DefaultIncluded => ReadBool(Parameters, DefaultIncludedKey, false);
    public bool HiddenWhenExcluded => ReadBool(Parameters, HiddenWhenExcludedKey, false);
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

    private static float ReadFloat(Dictionary<string, object> p, string key, float def)
    {
        if (p == null || !p.TryGetValue(key, out var v) || v == null)
            return def;
        switch (v)
        {
            case float f:
                return f;
            case double d:
                return (float)d;
            case int i:
                return i;
            case long l:
                return l;
            case string s when float.TryParse(s, out var fs):
                return fs;
        }
        if (
            v is JsonElement je
            && je.ValueKind == JsonValueKind.Number
            && je.TryGetDouble(out var jd)
        )
            return (float)jd;
        return def;
    }

    private static bool ReadBool(Dictionary<string, object> p, string key, bool def)
    {
        if (p == null || !p.TryGetValue(key, out var v) || v == null)
            return def;
        if (v is bool b)
            return b;
        if (v is string s && bool.TryParse(s, out var bs))
            return bs;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True)
                return true;
            if (je.ValueKind == JsonValueKind.False)
                return false;
        }
        return def;
    }

    /// <summary>
    /// Parse a seat-index list from a Parameters dictionary, tolerant of the JSON round-trip.
    /// </summary>
    public static HashSet<int> SeatSetFor(Dictionary<string, object> p, string key)
    {
        var set = new HashSet<int>();
        if (p == null || !p.TryGetValue(key, out var v) || v == null)
            return set;

        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in je.EnumerateArray())
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var ji))
                    set.Add(ji);
            return set;
        }

        if (v is System.Collections.IEnumerable e && v is not string)
        {
            foreach (var item in e)
                if (TryToInt(item, out var i))
                    set.Add(i);
        }
        return set;
    }

    private static bool TryToInt(object o, out int result)
    {
        result = 0;
        switch (o)
        {
            case int i:
                result = i;
                return true;
            case long l:
                result = (int)l;
                return true;
            case double d:
                result = (int)d;
                return true;
            case float f:
                result = (int)f;
                return true;
            case string s when int.TryParse(s, out var si):
                result = si;
                return true;
        }
        if (
            o is JsonElement je
            && je.ValueKind == JsonValueKind.Number
            && je.TryGetInt32(out var ji)
        )
        {
            result = ji;
            return true;
        }
        return false;
    }
}
