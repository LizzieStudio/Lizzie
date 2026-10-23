using System;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// Undo and redo. Those without targets walk the changes in their view's
/// <see cref="ICommandView.UndoScope"/>, and do nothing outside any view.
/// </summary>
public static class UndoCommands
{
    public static readonly Command Undo = new GlobalCommand
    {
        Name = new("app.undo"),
        Icon = CommandIcons.Undo,
        Caption = "Undo Mine",
        Tooltip = "Undoes the last change that you made in this view.",
        Keys = [Shortcuts.Ctrl(Key.Z)],
        SideEffects = v => Issue(Undo, TimelineUndo.Undo, Mine(v)),
    };

    public static readonly Command Redo = new GlobalCommand
    {
        Name = new("app.redo"),
        Icon = CommandIcons.Redo,
        Caption = "Redo Mine",
        Tooltip = "Redoes an undo that you just did in this view.",
        Keys = [Shortcuts.Ctrl(Key.Y), Shortcuts.Ctrl(Key.Z, shift: true)],
        SideEffects = v => Issue(Redo, TimelineUndo.Redo, Mine(v)),
    };

    public static readonly Command UndoOthers = new GlobalCommand
    {
        Name = new("app.undo_others"),
        Icon = CommandIcons.UndoOthers,
        Caption = "Undo Others",
        Tooltip = "Undoes the last change that any other player made in this view.",
        Keys = [Shortcuts.Ctrl(Key.Z, alt: true)],
        SideEffects = v => Issue(UndoOthers, TimelineUndo.Undo, Others(v)),
    };

    public static readonly Command RedoOthers = new GlobalCommand
    {
        Name = new("app.redo_others"),
        Icon = CommandIcons.RedoOthers,
        Caption = "Redo Others",
        Tooltip = "Redoes an undo that any other player just did in this view.",
        Keys = [Shortcuts.Ctrl(Key.Y, alt: true)],
        SideEffects = v => Issue(RedoOthers, TimelineUndo.Redo, Others(v)),
    };

    /// <summary>
    /// Undoes the newest change by anyone to the selected atoms, or to what's in them, like a deck's cards or a row's cells.
    /// The whole event or gesture is undone, even the parts that changed other atoms.
    /// </summary>
    public static readonly Command UndoChanges = new AtomCommand<Atom>
    {
        Name = new("record.undo_changes"),
        Icon = CommandIcons.UndoComponent,
        Caption = "Undo This",
        Tooltip = "Undoes the last change that affected this component, regardless of who did it.",
        ActsOn = Context.Selected | Context.Contents,
        SideEffects = (atoms, _) =>
        {
            var refs = atoms.Select(AtomRef.To).ToHashSet();
            Issue(UndoChanges, TimelineUndo.Undo, (_, atom) => refs.Contains(AtomRef.To(atom)));
        },
    };

    private static byte Me => Snowport.Clock.source;

    /// <summary>
    /// A filter for the player's own changes in the view.
    /// </summary>
    private static Func<byte, Atom, bool> Mine(ICommandView view) =>
        view == null ? null : (author, atom) => author == Me && view.UndoScope(atom);

    /// <summary>
    /// A filter for other players changes in the view.
    /// Selection events, however, are passed over. That's too far.
    /// </summary>
    private static Func<byte, Atom, bool> Others(ICommandView view) =>
        view == null
            ? null
            : (author, atom) => author != Me && atom is not Selection && view.UndoScope(atom);

    /// <summary>
    /// <see cref="TimelineUndo.Undo"/> or <see cref="TimelineUndo.Redo"/>.
    /// This is used to make it easier to set which one.
    /// </summary>
    private delegate UndoFlag Pick(Timeline timeline, byte me, Func<byte, Atom, bool> scope);

    /// <summary>
    /// Submits the flag that <paramref name="pick"/> finds in <paramref name="scope"/>, if any,
    /// as an event made by <paramref name="command"/>.
    /// </summary>
    private static void Issue(Command command, Pick pick, Func<byte, Atom, bool> scope)
    {
        if (EventSynchronizer.Instance == null || scope == null)
            return;

        // this creates a performance timeline in the debug console
        using var _ = DebugTimings.Measure(pick.Method.Name);
        if (pick(EventSynchronizer.Instance.Timeline, Me, scope) is { } flag)
            EventSynchronizer.Instance.Submit(TimelineEvent.Undoing(flag, command.Name));
    }
}
