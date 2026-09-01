using System;
using System.Text.Json.Serialization;
using Godot;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ComponentCreatedEvent), "ComponentCreated")]
[JsonDerivedType(typeof(ComponentDeletedEvent), "ComponentDeleted")]
[JsonDerivedType(typeof(ComponentRolledEvent), "ComponentRolled")]
[JsonDerivedType(typeof(ComponentFlippedEvent), "ComponentFlipped")]
[JsonDerivedType(typeof(ComponentShuffledEvent), "ComponentShuffled")]
[JsonDerivedType(typeof(ComponentsDraggedEvent), "ComponentsDragged")]
[JsonDerivedType(typeof(ComponentsDroppedEvent), "ComponentsDropped")]
[JsonDerivedType(typeof(ComponentsTransformedEvent), "ComponentsTransformed")]
public abstract class TableEvent
{
    public SnowportId Id { get; set; }
}

public class ComponentCreatedEvent : TableEvent
{
    public Guid PrototypeRef { get; set; }

    public string ComponentName { get; set; } = string.Empty;

    public VcSyncDto State { get; set; }
}

public class ComponentDeletedEvent : TableEvent
{
    public SnowportId ComponentRef { get; set; }
}

public class ComponentRolledEvent : TableEvent
{
    public SnowportId ComponentRef { get; set; }

    public int Side { get; set; }
}

public class ComponentFlippedEvent : TableEvent
{
    public SnowportId ComponentRef { get; set; }

    public bool FaceUp { get; set; }
}

public class ComponentShuffledEvent : TableEvent
{
    public SnowportId ComponentRef { get; set; }

    public ulong Seed { get; set; }
}

public class ComponentsDraggedEvent : TableEvent
{
    public DraggedComponent[] Components { get; set; } = Array.Empty<DraggedComponent>();
}

public struct DraggedComponent
{
    public SnowportId ComponentRef { get; set; }

    /// <summary>
    /// Table offset of the component from the dragging player's cursor during the drag.
    /// The component's position is the source player's cursor plus this offset.
    /// </summary>
    public Vector2 Offset { get; set; }
}

public class ComponentsDroppedEvent : TableEvent
{
    public DroppedComponent[] Components { get; set; } = Array.Empty<DroppedComponent>();
}

public struct DroppedComponent
{
    public SnowportId ComponentRef { get; set; }

    public Vector3 Position { get; set; }
}

/// <summary>
/// Instant move, reorientation, relocation, or reordering of one or more components. Carries the target
/// position, location, rotation, and ZOrder for each component. Fired whenever a component's transform
/// suddenly changes, like when it is drawn from or dropped into a container, added or removed
/// from a hand, or rotated with the rotate buttons.
/// </summary>
public class ComponentsTransformedEvent : TableEvent
{
    public TransformedComponent[] Components { get; set; } = Array.Empty<TransformedComponent>();
}

public struct TransformedComponent
{
    public SnowportId ComponentRef { get; set; }

    public VisualComponentBase.ComponentLocation Location { get; set; }

    public Vector3 Position { get; set; }

    /// <summary>Target rotation in radians.</summary>
    public Vector3 Rotation { get; set; }

    /// <summary>
    /// Where the event sends the component in the ZOrder, or Unset.
    /// </summary>
    public ZTarget ZTarget { get; set; }

    /// <summary>Separates components reordered by the same event. Higher ends up on top.</summary>
    public int ZSuborder { get; set; }

    /// <summary>
    /// Captures a component's current transform state so a caller can override only the fields it
    /// intends to change without clobbering the others.
    /// </summary>
    public static TransformedComponent Capture(VisualComponentBase component) =>
        new()
        {
            ComponentRef = component.Reference,
            Location = component.Location,
            Position = component.Position,
            Rotation = component.Rotation,
        };
}
