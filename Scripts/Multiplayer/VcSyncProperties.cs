using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The animation a component plays when a write is applied.
/// </summary>
public enum Transition
{
    None,
    Flip,
    Roll,
}

/// <summary>
/// A component's full replicated state. Every component write carries all of it.
/// </summary>
public record ComponentState
{
    /// <summary>
    /// The state last written for a component, minus the transition.
    /// </summary>
    public static ComponentState Of(VisualComponentBase c)
    {
        if (!ProjectService.Instance.GameObjects.TryGetState(c.Reference, out var s))
            throw new InvalidOperationException($"Component {c.Reference} has no written state.");
        return s with { Transition = Transition.None };
    }

    /// <summary>
    /// Captures a node that hasn't been written yet, such as a spawn preview.
    /// </summary>
    public static ComponentState Capture(VisualComponentBase c) =>
        new()
        {
            Id = c.Reference,
            PrototypeRef = c.PrototypeRef,
            Position = c.Position,
            Rotation = c.Rotation,
            DataSetRowIndex = c.DataSetRowIndex,
            DataSetRowId = c.DataSetRowId,
            Location = c.Location,
            ContainerRef = c.ContainerRef,
            Holder = c.Holder,
            ZOrder = c.ZOrder,
        };

    [JsonPropertyName("i")]
    public SnowTag Id { get; init; }

    [JsonPropertyName("pr")]
    public SnowTag PrototypeRef { get; init; }

    /// <summary>
    /// X relative to either the table or player cursor, in tenths of a millimeter.
    /// </summary>
    [JsonPropertyName("px")]
    public int X { get; init; }

    /// <summary>
    /// Z relative to either the table or player cursor, in tenths of a millimeter.
    /// </summary>
    [JsonPropertyName("pz")]
    public int Z { get; init; }

    /// <summary>
    /// Sets <see cref="X"/> and <see cref="Z"/> from a node position, rounding to the nearest
    /// tenth of a millimeter. Y is dropped since stacking derives height.
    /// </summary>
    [JsonIgnore]
    public Vector3 Position
    {
        init
        {
            X = CmToTenthMm(value.X);
            Z = CmToTenthMm(value.Z);
        }
    }

    /// <summary>
    /// The node position for this state with the provided <paramref name="y"/>.
    /// </summary>
    public Vector3 PositionAt(float y) => new(TenthMmToCm(X), y, TenthMmToCm(Z));

    /// <summary>
    /// A node's table position in tenths of a millimeter.
    /// </summary>
    public static (int X, int Z) TableKey(Vector3 nodePosition) =>
        (CmToTenthMm(nodePosition.X), CmToTenthMm(nodePosition.Z));

    // Node units are centimeters.
    private const float TenthMmPerUnit = 100f;

    private static int CmToTenthMm(float v) => (int)MathF.Round(v * TenthMmPerUnit);

    private static float TenthMmToCm(int v) => v / TenthMmPerUnit;

    [JsonPropertyName("r")]
    public Vector3 Rotation { get; init; }

    [JsonPropertyName("z")]
    public ZOrder ZOrder { get; init; }

    [JsonPropertyName("di")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int DataSetRowIndex { get; init; } = -1;

    [JsonPropertyName("dr")]
    public SnowTag DataSetRowId { get; init; } = SnowTag.Empty;

    [JsonPropertyName("l")]
    public VisualComponentBase.ComponentLocation Location { get; init; }

    /// <summary>
    /// The container that holds this component or <see cref="SnowTag.Empty"/>.
    /// </summary>
    [JsonPropertyName("c")]
    public SnowTag ContainerRef { get; init; } = SnowTag.Empty;

    /// <summary>
    /// While <see cref="VisualComponentBase.ComponentLocation.Cursor"/>, the Snowport source of the
    /// player holding it.
    /// </summary>
    [JsonPropertyName("h")]
    public byte Holder { get; init; }

    /// <summary>Reversible soft-delete flag.</summary>
    [JsonPropertyName("x")]
    public bool Deleted { get; init; }

    /// <summary>
    /// How this write animates. It starts when the write was made and is never copied
    /// forward, so it only ever describes the write that set it.
    /// </summary>
    [JsonPropertyName("t")]
    public Transition Transition { get; init; }

    public void ApplyToComponent(VisualComponentBase component)
    {
        component.PrototypeRef = PrototypeRef;
        // A held node is placed by its holder's cursor.
        if (Location != VisualComponentBase.ComponentLocation.Cursor)
            component.Position = PositionAt(component.Position.Y);
        component.Rotation = Rotation;
        component.ZOrder = ZOrder;
        component.DataSetRowIndex = DataSetRowIndex;
        component.DataSetRowId = DataSetRowId;
        component.Location = Location;
        component.ContainerRef = ContainerRef;
        component.Holder = Holder;
    }
}
