using System;
using System.Text.Json.Serialization;

public class TableEvent
{
    [JsonPropertyName("i")]
    public SnowportId Id { get; set; }

    [JsonPropertyName("a")]
    public TableAction Action { get; set; }

    [JsonPropertyName("e")]
    public Effect[] Effects { get; set; } = Array.Empty<Effect>();

    /// <summary>
    /// The undo group this event belongs to.
    /// Uses the SnowportId of the first event in the group.
    /// Uses <see cref="SnowportId.Empty"/> if it belongs to no group.
    /// </summary>
    [JsonPropertyName("g")]
    public SnowportId Group { get; set; }

    /// <summary>
    /// True on the event that ends its <see cref="Group"/>.
    /// </summary>
    [JsonPropertyName("c")]
    public bool Close { get; set; }

    /// <summary>What an undo of this event targets: its group, or itself.</summary>
    [JsonIgnore]
    public SnowportId Unit => Group == SnowportId.Empty ? Id : Group;

    /// <summary>Creates an event with a new SnowportId.</summary>
    public static TableEvent Now(TableAction action, Effect[] effects = null, bool close = false) =>
        new()
        {
            Id = Snowport.Clock.Create(),
            Action = action,
            Effects = effects ?? Array.Empty<Effect>(),
            Close = close,
        };
}
