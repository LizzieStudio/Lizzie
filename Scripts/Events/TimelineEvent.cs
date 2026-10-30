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
    /// In which view the player made this event, or null.
    /// </summary>
    public ViewPlace? Place { get; set; }

    /// <summary>
    /// The atoms this event writes, each with its whole value.
    /// </summary>
    public Atom[] Writes { get; set; } = Array.Empty<Atom>();

    /// <summary>
    /// The atoms this event unsets. A write of the same atom in <see cref="Writes"/> wins over them.
    /// </summary>
    public Unset[] Unsets { get; set; } = Array.Empty<Unset>();

    /// <summary>
    /// Set on an undo or a redo, which changes nothing itself but reverses an earlier entry in history.
    /// </summary>
    public UndoFlag Undo { get; set; }

    /// <summary>
    /// The transition this event ends, or <see cref="Transition.None"/>.
    /// The transition's drafts stop counting, so what this event writes is all of it that's kept.
    /// Ending it with no writes cancels it.
    /// </summary>
    public Transition Ends { get; set; }

    /// <summary>
    /// Whether this is an admin event, which belongs to the project rather than a player.
    /// </summary>
    [JsonIgnore]
    public bool IsAdmin => Id.source == Snowport.AdminSource;

    /// <summary>Creates an event with a new SnowportId.</summary>
    public static TimelineEvent Now(
        IChange[] changes,
        CommandName? command = null,
        ViewPlace? place = null
    ) => Of(SnowportId.Create(), changes, command, place);

    /// <summary>
    /// Creates an admin event, which don't belong to any player.
    /// </summary>
    public static TimelineEvent Admin(IChange[] changes) =>
        Of(SnowportId.CreateAdmin(), changes, null, null);

    private static TimelineEvent Of(
        SnowportId id,
        IChange[] changes,
        CommandName? command,
        ViewPlace? place
    ) =>
        new()
        {
            Id = id,
            Command = command,
            Place = place,
            Writes = changes.OfType<Atom>().ToArray(),
            Unsets = changes.OfType<Unset>().ToArray(),
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
}

/// <summary>
/// Marks an undo or a redo, which reverses one earlier entry in history:
/// <list type="bullet">
/// <item>another undo/redo, by its Id</item>
/// <item>a regular event, by its Id</item>
/// </list>
/// </summary>
public class UndoFlag
{
    /// <summary>
    /// The id of the event that this event reverses.
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
