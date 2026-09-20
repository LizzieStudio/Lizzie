using System;
using System.Text.Json.Serialization;

public record Prototype : IReplicated
{
    //unique identifier for this prototype. Should be generated when the component is created, and never changed.
    public SnowTag Id { get; init; }

    /// <summary>
    /// Reversible soft-delete flag.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>The id of the last event that updated this prototype.</summary>
    public SnowportId LastUpdateId { get; init; }

    public string Name { get; init; }

    public ComponentParameters Parameters { get; init; }

    [JsonIgnore]
    public VisualComponentBase.VisualComponentType Type => Parameters?.ComponentType ?? default;

    public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
        this with { Id = id, LastUpdateId = lastUpdateId };
}
