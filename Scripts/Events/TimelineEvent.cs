using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace Lizzie.Replication.Machinery;

public class TimelineEvent
{
    public SnowportId Id { get; set; }

    /// <summary>
    /// The command that made this event, or null if there wasn't one.
    /// </summary>
    public CommandName? Command { get; set; }

    /// <summary>
    /// The atoms this event writes, each with its whole value.
    /// </summary>
    public Atom[] Writes { get; set; } = Array.Empty<Atom>();

    /// <summary>
    /// The atoms this event unsets, as the player saw them. An unset beats a write of the same atom in <see cref="Writes"/>.
    /// </summary>
    public Atom[] Unsets { get; set; } = Array.Empty<Atom>();

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
    /// How this event ends its <see cref="Group"/>, if it does.
    /// <list type="bullet">
    /// <item>Before an event that ends it, the group is "open" and undo/redo ignore it.</item>
    /// <item>After a <see cref="GroupEnd.Closed"/> event the group is "closed" and undo/redo remove or reapply the whole group together.</item>
    /// <item>
    /// A <see cref="GroupEnd.Canceled"/> group is effectively undone, and undo and redo pass over it.
    /// Only an open group can be canceled: a cancel after the group's close does nothing.
    /// </item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// We cancel because using Undo would put a history event for Redo to reverse, which is the wrong UX.
    /// </remarks>
    public GroupEnd End { get; set; }

    /// <summary>What an undo of this event targets: its group, or itself.</summary>
    [JsonIgnore]
    public SnowportId Unit => Group == SnowportId.Empty ? Id : Group;

    /// <summary>
    /// Whether this is an admin event, which belongs to the project rather than a player.
    /// </summary>
    [JsonIgnore]
    public bool IsAdmin => Id.source == Snowport.AdminSource;

    /// <summary>Creates an event with a new SnowportId.</summary>
    public static TimelineEvent Now(IChange[] changes, CommandName? command = null) =>
        Of(SnowportId.Create(), changes, command);

    /// <summary>
    /// Creates an admin event, which don't belong to any player.
    /// </summary>
    public static TimelineEvent Admin(IChange[] changes) =>
        Of(SnowportId.CreateAdmin(), changes, null);

    private static TimelineEvent Of(SnowportId id, IChange[] changes, CommandName? command) =>
        new()
        {
            Id = id,
            Command = command,
            Writes = changes.OfType<Atom>().ToArray(),
            Unsets = changes.OfType<Unset>().Select(u => u.Atom).ToArray(),
        };

    /// <summary>
    /// Creates an undo or a redo, issued by <paramref name="command"/> if there was one.
    /// <paramref name="admin"/> makes it an admin event, which an undo command won't reverse.
    /// </summary>
    public static TimelineEvent Undoing(
        UndoFlag flag,
        CommandName? command = null,
        bool admin = false
    ) =>
        new()
        {
            Id = admin ? SnowportId.CreateAdmin() : SnowportId.Create(),
            Command = command,
            Undo = flag,
        };

    /// <summary>
    /// Creates an event that does nothing but closes <paramref name="group"/>,
    /// for a gesture that ended without its closing event.
    /// This is only really used as a fallback.
    /// </summary>
    public static TimelineEvent Closing(SnowportId group) =>
        new()
        {
            Id = SnowportId.Create(),
            Group = group,
            End = GroupEnd.Closed,
        };

    /// <summary>
    /// Creates an event that cancels the open <paramref name="group"/>, so it never happened.
    /// </summary>
    public static TimelineEvent Canceling(SnowportId group) =>
        new()
        {
            Id = SnowportId.Create(),
            Group = group,
            End = GroupEnd.Canceled,
        };
}

/// <summary>
/// How an event ends its undo group, in <see cref="TimelineEvent.End"/>.
/// </summary>
public enum GroupEnd
{
    /// <summary>
    /// The default value, which does nothing.
    /// </summary>
    None,

    /// <summary>
    /// It completes its group, which are Undone or Redone together.
    /// </summary>
    Closed,

    /// <summary>
    /// It cancels its open group, making it as if it never happened.
    /// </summary>
    Canceled,
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
