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

    /// <summary>Creates an event with a new SnowportId.</summary>
    public static TableEvent Now(TableAction action, params Effect[] effects) =>
        new()
        {
            Id = Snowport.Clock.Create(),
            Action = action,
            Effects = effects ?? Array.Empty<Effect>(),
        };
}
