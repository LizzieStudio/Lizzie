using System;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

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
        Keys = [Shortcuts.Ctrl(Key.Z)],
        SideEffects = v => Issue(Undo, UndoLog.Undo, Mine(v)),
    };

    public static readonly Command Redo = new GlobalCommand
    {
        Name = new("app.redo"),
        Icon = CommandIcons.Redo,
        Caption = "Redo Mine",
        Keys = [Shortcuts.Ctrl(Key.Y), Shortcuts.Ctrl(Key.Z, shift: true)],
        SideEffects = v => Issue(Redo, UndoLog.Redo, Mine(v)),
    };

    public static readonly Command UndoOthers = new GlobalCommand
    {
        Name = new("app.undo_others"),
        Icon = CommandIcons.UndoOthers,
        Caption = "Undo Others",
        Keys = [Shortcuts.Ctrl(Key.Z, alt: true)],
        SideEffects = v => Issue(UndoOthers, UndoLog.Undo, Others(v)),
    };

    public static readonly Command RedoOthers = new GlobalCommand
    {
        Name = new("app.redo_others"),
        Icon = CommandIcons.RedoOthers,
        Caption = "Redo Others",
        Keys = [Shortcuts.Ctrl(Key.Y, alt: true)],
        SideEffects = v => Issue(RedoOthers, UndoLog.Redo, Others(v)),
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
        ActsOn = Context.Selected | Context.Contents,
        SideEffects = (atoms, _) =>
        {
            var refs = atoms.Select(AtomRef.To).ToHashSet();
            Issue(UndoChanges, UndoLog.Undo, (_, atom) => refs.Contains(AtomRef.To(atom)));
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
    /// <see cref="UndoLog.Undo"/> or <see cref="UndoLog.Redo"/>.
    /// This is used to make it easier to set which one.
    /// </summary>
    private delegate UndoFlag Pick(EventLog log, byte me, Func<byte, Atom, bool> scope);

    /// <summary>
    /// Submits the flag that <paramref name="pick"/> finds in <paramref name="scope"/>, if any,
    /// as an event made by <paramref name="command"/>.
    /// </summary>
    private static void Issue(Command command, Pick pick, Func<byte, Atom, bool> scope)
    {
        if (EventSynchronizer.Instance == null || scope == null)
            return;

        // this creates a performance log in the debug console
        using var _ = DebugTimings.Measure(pick.Method.Name);
        if (pick(EventSynchronizer.Instance.Log, Me, scope) is { } flag)
            EventSynchronizer.Instance.Submit(TableEvent.Undoing(flag, command.Name));
    }
}
