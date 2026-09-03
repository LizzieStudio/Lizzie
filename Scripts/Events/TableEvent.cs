using System;

public class TableEvent
{
    public SnowportId Id { get; set; }

    public TableAction Action { get; set; }

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
