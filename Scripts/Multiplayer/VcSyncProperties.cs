using System;
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
        Position = component.Position;
        Rotation = component.Rotation;
        LogicalVisible = component.LogicalVisible;
        ZOrder = component.ZOrder;
        DataSetRow = component.DataSetRow;
        Location = component.Location;
        ContainerRef = component.ContainerRef;
    }

    // Uses Godot native Vector3 serialization
    public Vector3 Position { get; set; }

    // Uses Godot native Vector3 serialization
    public Vector3 Rotation { get; set; }

    public bool LogicalVisible { get; set; }

    public ZOrder ZOrder { get; set; }

    public string DataSetRow { get; set; }
    public VisualComponentBase.ComponentLocation Location { get; set; }

    /// <summary>
    /// The container that holds this component or <see cref="SnowportId.Empty"/>.
    /// </summary>
    public SnowportId ContainerRef { get; set; } = SnowportId.Empty;

    public void ApplyToComponent(VisualComponentBase component)
    {
        component.Position = Position;
        component.Rotation = Rotation;
        component.LogicalVisible = LogicalVisible;
        component.ZOrder = ZOrder;
        component.DataSetRow = DataSetRow;
        component.Location = Location;
        component.ContainerRef = ContainerRef;
    }
}
