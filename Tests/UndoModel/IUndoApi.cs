using System;
using System.Collections.Generic;
using System.Linq;
using Log = System.Collections.Generic.OrderedDictionary<SnowportId, TableEvent>;

/// <summary>
/// The undo API, so the same properties can check any implementation against another.
/// </summary>
public interface IUndoApi
{
    string Name { get; }

    UndoFlag Undo(Log log, byte me, Func<byte, Replicated, bool> scope);

    UndoFlag Redo(Log log, byte me, Func<byte, Replicated, bool> scope);

    List<SnowportId> OpenGroups(Log log, byte source);

    IEnumerable<TableEvent> Changes(Log log, TableEvent e);

    IEnumerable<TableEvent> InEffect(Log log);

    /// <summary>Whether the event's history entry is out of effect: for an action, whether it's undone.</summary>
    bool IsUndone(Log log, TableEvent e) => !InEffect(log).Contains(e);
}

/// <summary>The game's implementation, in Scripts/Events/UndoLog.cs.</summary>
public sealed class CurrentUndo : IUndoApi
{
    public string Name => "current";

    public UndoFlag Undo(Log log, byte me, Func<byte, Replicated, bool> scope) =>
        UndoLog.Undo(log, me, scope);

    public UndoFlag Redo(Log log, byte me, Func<byte, Replicated, bool> scope) =>
        UndoLog.Redo(log, me, scope);

    public List<SnowportId> OpenGroups(Log log, byte source) => UndoLog.OpenGroups(log, source);

    public IEnumerable<TableEvent> Changes(Log log, TableEvent e) => UndoLog.Changes(log, e);

    public IEnumerable<TableEvent> InEffect(Log log) => UndoLog.InEffect(log);
}
