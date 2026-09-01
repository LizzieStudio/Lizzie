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
