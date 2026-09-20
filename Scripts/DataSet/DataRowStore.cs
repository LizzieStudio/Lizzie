using System.Collections.Generic;
using Godot;

/// <summary>
/// Tracks dataset rows as last-write-wins registers, keyed by row <see cref="SnowTag"/>.
/// </summary>
public partial class DataRowStore : ReplicatedStore<DataRow>
{
    private static DataRowStore _instance;
    public static DataRowStore Instance => _instance;

    [Signal]
    public delegate void DataRowsChangedEventHandler(int[] ids);

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += OnEventApplied;
    }

    public override void _ExitTree()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied -= OnEventApplied;

        if (_instance == this)
            _instance = null;
    }

    protected override IDictionary<SnowTag, DataRow> Store =>
        ProjectService.Instance?.CurrentProject?.DataRows;

    protected override void NotifyChanged(IReadOnlyList<SnowTag> ids) =>
        EmitSignal(SignalName.DataRowsChanged, SnowTag.ToValues(ids));

    /// <summary>Rebuilds the rows affected by an undo from the event log.</summary>
    protected override void OnUndo(UndoAction undo)
    {
        var store = Store;
        var log = EventSynchronizer.Instance?.Events;
        if (store == null || log == null)
            return;

        var undone = UndoLog.ComputeUndone(log);
        var changed = new List<SnowTag>();

        foreach (var id in UndoLog.ResolveAffectedRows(log, undo.Target))
        {
            var winner = UndoLog.LatestReplicated<DataRow>(log, id, undone);
            if (winner == null)
                store.Remove(id);
            else
                store[id] = winner;
            changed.Add(id);
        }

        if (changed.Count > 0)
            NotifyChanged(changed);
    }
}
