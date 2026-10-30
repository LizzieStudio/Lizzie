using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// Undo and redo. Those without targets walk the changes made in their view's
/// <see cref="ICommandView.Place"/>, and do nothing outside any place.
/// </summary>
public static class UndoCommands
{
    [Command("app.undo")]
    public static readonly Command Undo = new GlobalCommand
    {
        Icon = CommandIcons.Undo,
        Caption = "Undo Mine",
        Tooltip = "Undoes the last change that you made in this view.",
        Keys = [Shortcuts.Ctrl(Key.Z)],
        Effects = (R, v) =>
        {
            Issue(Undo, TimelineUndo.Undo, MyScopeIn(R, v));
            return null;
        },
    };

    [Command("app.redo")]
    public static readonly Command Redo = new GlobalCommand
    {
        Icon = CommandIcons.Redo,
        Caption = "Redo Mine",
        Tooltip = "Redoes an undo that you just did in this view.",
        Keys = [Shortcuts.Ctrl(Key.Y), Shortcuts.Ctrl(Key.Z, shift: true)],
        Effects = (R, v) =>
        {
            Issue(Redo, TimelineUndo.Redo, MyScopeIn(R, v));
            return null;
        },
    };

    [Command("app.undo_others")]
    public static readonly Command UndoOthers = new GlobalCommand
    {
        Icon = CommandIcons.UndoOthers,
        Caption = "Undo Others",
        Tooltip = "Undoes the last change that any other player made in this view.",
        Keys = [Shortcuts.Ctrl(Key.Z, alt: true)],
        Effects = (R, v) =>
        {
            Issue(UndoOthers, TimelineUndo.Undo, OthersScopeIn(R, v));
            return null;
        },
    };

    [Command("app.redo_others")]
    public static readonly Command RedoOthers = new GlobalCommand
    {
        Icon = CommandIcons.RedoOthers,
        Caption = "Redo Others",
        Tooltip = "Redoes an undo that any other player just did in this view.",
        Keys = [Shortcuts.Ctrl(Key.Y, alt: true)],
        Effects = (R, v) =>
        {
            Issue(RedoOthers, TimelineUndo.Redo, OthersScopeIn(R, v));
            return null;
        },
    };

    /// <summary>
    /// Undoes the newest change by anyone to the selected atoms, or to what's in them, like a deck's cards or a row's cells.
    /// The whole event is undone, even the parts that changed other atoms.
    /// </summary>
    [Command("record.undo_changes")]
    public static readonly Command UndoChanges = new AtomCommand<Atom>
    {
        Icon = CommandIcons.UndoComponent,
        Caption = "Undo This",
        Tooltip = "Undoes the last change that affected this component, regardless of who did it.",
        ActsOn = Context.Selected | Context.Contents,
        Effects = (_, atoms) =>
        {
            Issue(
                UndoChanges,
                TimelineUndo.Undo,
                UndoScopes.Changing(
                    EventSynchronizer.Instance?.Timeline,
                    atoms.Select(IAtomRef.To).ToHashSet()
                )
            );
            return null;
        },
    };

    private static byte Me => Snowport.Clock.source;

    private static TimelineUndo.Scope MyScopeIn(IAtomReader R, ICommandView view) =>
        view?.Place(R) is { } place ? UndoScopes.Mine(Me, place) : null;

    private static TimelineUndo.Scope OthersScopeIn(IAtomReader R, ICommandView view) =>
        view?.Place(R) is { } place ? UndoScopes.Others(Me, place) : null;

    /// <summary>
    /// <see cref="TimelineUndo.Undo"/> or <see cref="TimelineUndo.Redo"/>.
    /// This is used to make it easier to set which one.
    /// </summary>
    private delegate UndoFlag Pick(Timeline timeline, byte me, TimelineUndo.Scope scope);

    /// <summary>
    /// Submits the flag that <paramref name="pick"/> finds in <paramref name="scope"/>, if any,
    /// as an event made by <paramref name="command"/>.
    /// </summary>
    private static void Issue(Command command, Pick pick, TimelineUndo.Scope scope)
    {
        if (EventSynchronizer.Instance == null || scope == null)
            return;

        // this creates a performance timeline in the debug console
        using var _ = DebugTimings.Measure(pick.Method.Name);
        if (pick(EventSynchronizer.Instance.Timeline, Me, scope) is { } flag)
            EventSynchronizer.Instance.Submit(TimelineEvent.Undoing(flag, command.Name));
    }
}
