using System;
using System.Text.Json.Serialization;

public class TableEvent
{
    public SnowportId Id { get; set; }

    /// <summary>
    /// The command that made this event, or null if there wasn't one.
    /// </summary>
    public CommandName? Command { get; set; }

    public Effect[] Effects { get; set; } = Array.Empty<Effect>();

    /// <summary>
    /// Set on an undo or a redo, which changes nothing itself but reverses an earlier entry in history.
    /// </summary>
    public UndoFlag Undo { get; set; }

    /// <summary>
    /// The undo group this event belongs to.
    /// Uses the SnowportId of the first event in the group.
    /// Uses <see cref="SnowportId.Empty"/> if it belongs to no group.
    /// </summary>
    public SnowportId Group { get; set; }

    /// <summary>
    /// True on the event that finishes its <see cref="Group"/>.
    /// <list type="bullet">
    /// <item>Before a close event the group is "open" and undo/redo ignore it.</item>
    /// <item>After a close event the group is "closed" and undo/redo remove or reapply the whole group together.</item>
    /// </list>
    /// </summary>
    public bool Close { get; set; }

    /// <summary>What an undo of this event targets: its group, or itself.</summary>
    [JsonIgnore]
    public SnowportId Unit => Group == SnowportId.Empty ? Id : Group;

    /// <summary>
    /// Whether this is an admin event, which belongs to the table rather than a player.
    /// </summary>
    [JsonIgnore]
    public bool IsAdmin => Id.source == Snowport.AdminSource;

    /// <summary>Creates an event with a new SnowportId.</summary>
    public static TableEvent Now(Effect[] effects, CommandName? command = null) =>
        new()
        {
            Id = Snowport.Clock.Create(),
            Command = command,
            Effects = effects ?? Array.Empty<Effect>(),
        };

    /// <summary>
    /// Creates an admin event, which don't belong to any player.
    /// </summary>
    public static TableEvent Admin(Effect[] effects) =>
        new() { Id = Snowport.Clock.CreateAdmin(), Effects = effects ?? Array.Empty<Effect>() };

    /// <summary>
    /// Creates an undo or a redo, issued by <paramref name="command"/> if there was one.
    /// <paramref name="admin"/> makes it an admin event, which an undo command won't reverse.
    /// </summary>
    public static TableEvent Undoing(
        UndoFlag flag,
        CommandName? command = null,
        bool admin = false
    ) =>
        new()
        {
            Id = admin ? Snowport.Clock.CreateAdmin() : Snowport.Clock.Create(),
            Command = command,
            Undo = flag,
        };

    /// <summary>
    /// Creates an event that does nothing but closes <paramref name="group"/>,
    /// for a gesture that ended without its closing event.
    /// This is only really used as a fallback.
    /// </summary>
    public static TableEvent Closing(SnowportId group) =>
        new()
        {
            Id = Snowport.Clock.Create(),
            Group = group,
            Close = true,
        };
}

/// <summary>
/// Marks an undo or a redo, which reverses one earlier entry in history:
/// <list type="bullet">
/// <item>another undo/redo, by its Id</item>
/// <item>a regular event, by its Id</item>
/// <item>a group of events, by their group id (the id of the first event in the group)</item>
/// </list>
/// </summary>
public class UndoFlag
{
    /// <summary>
    /// The id of the event or group that this event reverses.
    /// It must be older than this event.
    /// </summary>
    public SnowportId Reverses { get; init; }

    /// <summary>
    /// True when triggered by a Redo action.
    /// When true, <see cref="Reverses"/> should be an Undo event.
    /// </summary>
    public bool ByRedo { get; init; }

    /// <summary>
    /// When <see cref="Reverses"/> is another Undo/Redo event, <see cref="Also"/> can reference other Undo/Redo events to reverse them too.
    /// This is only used for extreme edge-cases where, for example, two Undo events target the same event,
    /// which should only happen when two players trigger an undo at nearly the same time.
    /// That works fine, at first, but a Redo event then needs to target both those Undo events to work, giving us <see cref="Also"/>.
    /// </summary>
    public SnowportId[] Also { get; init; }
}
