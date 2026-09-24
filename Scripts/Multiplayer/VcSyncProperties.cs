using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// This class captures all the properties that need to be synced across the network for a visual component. This is used to ensure that all clients have the same state for each component, and to minimize the amount of data that needs to be sent over the network by only syncing relevant properties.
/// </summary>
public class VcSyncDto
{
    /// <summary>
    /// Need a parameterless constructor for JSON deserialization. This is used when receiving data from the network and creating a new instance of this class to apply the properties to a visual component.
    /// </summary>
    public VcSyncDto() { }

    public VcSyncDto(VisualComponentBase component)
    {
        Position =
            component.Location == VisualComponentBase.ComponentLocation.Cursor
                ? component.CursorOffset
                : component.Position;
        Rotation = component.Rotation;
        ZOrder = component.ZOrder;
        DataSetRowIndex = component.DataSetRowIndex;
        DataSetRowId = component.DataSetRowId;
        Location = component.Location;
        ContainerRef = component.ContainerRef;
        LastMoveId = component.LastMoveId;
    }

    /// <summary>
    /// Captures a component's live transform.
    /// </summary>
    public static VcSyncDto CaptureLive(VisualComponentBase c) =>
        new()
        {
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
            LastMoveId = SnowportId.Empty,
        };

    [JsonPropertyName("p")]
    public Vector3 Position { get; set; }

    [JsonPropertyName("r")]
    public Vector3 Rotation { get; set; }

    [JsonPropertyName("z")]
    public ZOrder ZOrder { get; set; }

    [JsonPropertyName("di")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int DataSetRowIndex { get; set; } = -1;

    [JsonPropertyName("dr")]
    public SnowTag DataSetRowId { get; set; } = SnowTag.Empty;

    [JsonPropertyName("l")]
    public VisualComponentBase.ComponentLocation Location { get; set; }

    /// <summary>
    /// The container that holds this component or <see cref="SnowTag.Empty"/>.
    /// </summary>
    [JsonPropertyName("c")]
    public SnowTag ContainerRef { get; set; } = SnowTag.Empty;

    /// <summary>
    /// The last event that moved this component with a transform.
    /// </summary>
    [JsonPropertyName("m")]
    public SnowportId LastMoveId { get; set; } = SnowportId.Empty;

    public void ApplyToComponent(VisualComponentBase component)
    {
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
