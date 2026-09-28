using System;
using System.Linq;
using Godot;

/// <summary>
/// Undo and redo. Those without targets walk the changes in their view's
/// <see cref="ICommandView.UndoScope"/>, and do nothing outside any view.
/// </summary>
public static class UndoCommands
{
    public static readonly Command Undo = new GlobalCommand
    {
        Id = new("app.undo"),
        Icon = "res://Textures/UI/undo.svg",
        Caption = "Undo Mine",
        Keys = [Shortcuts.Ctrl(Key.Z)],
        SideEffects = v => IssueUndo(Scope(v, UndoStream.Own), UndoStream.Own),
    };

    public static readonly Command Redo = new GlobalCommand
    {
        Id = new("app.redo"),
        Icon = "res://Textures/UI/redo.svg",
        Caption = "Redo Mine",
        Keys = [Shortcuts.Ctrl(Key.Y), Shortcuts.Ctrl(Key.Z, shift: true)],
        SideEffects = v => IssueRedo(Scope(v, UndoStream.Own), UndoStream.Own),
    };

    public static readonly Command UndoOthers = new GlobalCommand
    {
        Id = new("app.undo_others"),
        Icon = "res://Textures/UI/undo_others.svg",
        Caption = "Undo Others",
        Keys = [Shortcuts.Ctrl(Key.Z, alt: true)],
        SideEffects = v => IssueUndo(Scope(v, UndoStream.Others), UndoStream.Others),
    };

    public static readonly Command RedoOthers = new GlobalCommand
    {
        Id = new("app.redo_others"),
        Icon = "res://Textures/UI/redo_others.svg",
        Caption = "Redo Others",
        Keys = [Shortcuts.Ctrl(Key.Y, alt: true)],
        SideEffects = v => IssueRedo(Scope(v, UndoStream.Others), UndoStream.Others),
    };

    /// <summary>
    /// Undoes the newest change by anyone to the components, or to what's in them, like a deck's cards.
    /// The whole event or gesture is undone, even the parts that changed other components.
    /// </summary>
    public static readonly Command UndoComponentChanges = new RecordCommand<ComponentState>
    {
        Id = new("component.undo_changes"),
        Icon = "res://Textures/UI/undo_component.svg",
        Caption = "Undo This",
        IncludesContents = true,
        SideEffects = (cs, _) =>
        {
            var ids = cs.Select(c => c.Id).ToHashSet();
            IssueUndo(fx => ids.Contains(fx.Id), UndoStream.Anyone);
        },
    };

    private static void IssueUndo(Func<Effect, bool> scope, UndoStream stream)
    {
        var log = EventSynchronizer.Instance?.EventLog;
        // Nothing is undone in the middle of a gesture.
        if (log == null || EventSynchronizer.Instance.InGroup || scope == null)
            return;
        using var _ = DebugTimings.Measure("Undo");
        if (
            UndoLog.ComputeUndoTarget(log, Snowport.Clock.source, scope, stream)
            is SnowportId target
        )
            EventSynchronizer.Instance.Submit(TableEvent.Now(new UndoAction { Target = target }));
    }

    /// <summary>A redo is an undo that targets the most recent live undo.</summary>
    private static void IssueRedo(Func<Effect, bool> scope, UndoStream stream)
    {
        var log = EventSynchronizer.Instance?.EventLog;
        // Nothing is undone in the middle of a gesture.
        if (log == null || EventSynchronizer.Instance.InGroup || scope == null)
            return;
        using var _ = DebugTimings.Measure("Redo");
        if (
            UndoLog.ComputeRedoTarget(log, Snowport.Clock.source, scope, stream)
            is SnowportId target
        )
            EventSynchronizer.Instance.Submit(
                TableEvent.Now(new UndoAction { Target = target, Redo = true })
            );
    }

    /// <summary>
    /// The view's undo scope, or null outside any view. Another player's selection is theirs
    /// to change, so undoing other players' actions passes over it.
    /// </summary>
    private static Func<Effect, bool> Scope(ICommandView view, UndoStream stream)
    {
        if (view == null)
            return null;
        if (stream != UndoStream.Others)
            return view.UndoScope;
        return fx => view.UndoScope(fx) && fx is not UpdateReplicatedEffect<Selection>;
    }
}
