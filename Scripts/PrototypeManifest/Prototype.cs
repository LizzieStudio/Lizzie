using System;
using System.Text.Json.Serialization;

public class Prototype : IReplicated
{
    public Prototype() { }

    //unique identifier for this prototype. Should be generated when the component is created, and never changed.
    public SnowTag Id { get; set; }

    /// <summary>
    /// Reversible soft-delete flag.
    /// </summary>
    public bool Deleted { get; set; }

    /// <summary>The id of the last event that updated this prototype.</summary>
    public SnowportId LastUpdateId { get; set; }

    public string Name { get; set; }

    public ComponentParameters Parameters { get; set; }

    [JsonIgnore]
    public VisualComponentBase.VisualComponentType Type => Parameters?.ComponentType ?? default;
}
