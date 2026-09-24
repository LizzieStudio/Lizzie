using System;
using System.Text.Json.Serialization;
using Godot;

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

    [JsonPropertyName("p")]
    public Vector3 Position { get; init; }

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
    /// The last event that moved this component with a transform.
    /// </summary>
    [JsonPropertyName("m")]
    public SnowportId LastMoveId { get; init; } = SnowportId.Empty;

    public void ApplyToComponent(VisualComponentBase component)
    {
        component.PrototypeRef = PrototypeRef;
        // While dragged, Position carries the cursor-relative offset.
        // This might be a bad idea if it becomes hard to keep in-sync.
        if (Location == VisualComponentBase.ComponentLocation.Cursor)
            component.CursorOffset = Position;
        else
            component.Position = Position;
        component.Rotation = Rotation;
        component.ZOrder = ZOrder;
        component.DataSetRowIndex = DataSetRowIndex;
        component.DataSetRowId = DataSetRowId;
        component.Location = Location;
        component.ContainerRef = ContainerRef;
        component.LastMoveId = LastMoveId;
    }
}
