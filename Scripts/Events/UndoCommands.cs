using Godot;

/// <summary>Undo and redo, which work anywhere with no targets.</summary>
public static class UndoCommands
{
    public static readonly Command Undo = new GlobalCommand
    {
        Id = new("app.undo"),
        Caption = "Undo",
        Keys = [Shortcuts.Ctrl(Key.Z)],
        SideEffects = IssueUndo,
    };

    public static readonly Command Redo = new GlobalCommand
    {
        Id = new("app.redo"),
        Caption = "Redo",
        Keys = [Shortcuts.Ctrl(Key.Y), Shortcuts.Ctrl(Key.Z, shift: true)],
        SideEffects = IssueRedo,
    };

    private static void IssueUndo()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        // Nothing is undone in the middle of a gesture.
        if (log == null || EventSynchronizer.Instance.InGroup)
            return;
        using var _ = DebugTimings.Measure("Undo");
        if (UndoLog.ComputeUndoTarget(log, Snowport.Clock.source) is SnowportId target)
            EventSynchronizer.Instance.Submit(TableEvent.Now(new UndoAction { Target = target }));
    }

    /// <summary>A redo is an undo that targets the most recent live undo.</summary>
    private static void IssueRedo()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        // Nothing is undone in the middle of a gesture.
        if (log == null || EventSynchronizer.Instance.InGroup)
            return;
        using var _ = DebugTimings.Measure("Redo");
        if (UndoLog.ComputeRedoTarget(log, Snowport.Clock.source) is SnowportId target)
            EventSynchronizer.Instance.Submit(
                TableEvent.Now(new UndoAction { Target = target, Redo = true })
            );
    }
}
