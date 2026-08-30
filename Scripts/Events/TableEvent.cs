using System;
using System.Text.Json.Serialization;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ComponentCreatedEvent), "ComponentCreated")]
[JsonDerivedType(typeof(ComponentDeletedEvent), "ComponentDeleted")]
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
