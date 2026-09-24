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
    /// Captures a component's live state.
    /// </summary>
    public static ComponentState Capture(VisualComponentBase c) =>
        new()
        {
            Id = c.Reference,
            PrototypeRef = c.PrototypeRef,
            Position =
                c.Location == VisualComponentBase.ComponentLocation.Cursor
                    ? c.CursorOffset
                    : c.Position,
            Rotation = c.Rotation,
            DataSetRowIndex = c.DataSetRowIndex,
            DataSetRowId = c.DataSetRowId,
            Location = c.Location,
            ContainerRef = c.ContainerRef,
            ZOrder = c.ZOrder,
        };

    [JsonPropertyName("i")]
    public SnowTag Id { get; init; }

    [JsonPropertyName("pr")]
    public SnowTag PrototypeRef { get; init; }

    /// <summary>
    /// X on the table, in tenths of a millimeter.
    /// </summary>
    [JsonPropertyName("px")]
    public int X { get; init; }

    /// <summary>
    /// Z on the table, in tenths of a millimeter.
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

    // Node units are centimeters.
    private const float TenthMmPerUnit = 100f;

    private static int CmToTenthMm(float v) => (int)(v * TenthMmPerUnit);

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
        // While dragged, Position carries the cursor-relative offset.
        // This might be a bad idea if it becomes hard to keep in-sync.
        if (Location == VisualComponentBase.ComponentLocation.Cursor)
            component.CursorOffset = PositionAt(component.CursorOffset.Y);
        else
            component.Position = PositionAt(component.Position.Y);
        component.Rotation = Rotation;
        component.ZOrder = ZOrder;
        component.DataSetRowIndex = DataSetRowIndex;
        component.DataSetRowId = DataSetRowId;
        component.Location = Location;
        component.ContainerRef = ContainerRef;
    }
}
