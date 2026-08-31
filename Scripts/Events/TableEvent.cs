using System;
using System.Text.Json.Serialization;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ComponentCreatedEvent), "ComponentCreated")]
[JsonDerivedType(typeof(ComponentDeletedEvent), "ComponentDeleted")]
[JsonDerivedType(typeof(ComponentRolledEvent), "ComponentRolled")]
[JsonDerivedType(typeof(ComponentFlippedEvent), "ComponentFlipped")]
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
