using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks dataset definitions as a last-write-wins register.
/// </summary>
public partial class DataSetManager : ReplicatedStore<DataSet>
{
    private static DataSetManager _instance;
    public static DataSetManager Instance => _instance;

    [Signal]
    public delegate void DataSetsChangedEventHandler(long[] ids);

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

    protected override IDictionary<SnowportId, DataSet> Store =>
        ProjectService.Instance?.CurrentProject?.Datasets;

    protected override void NotifyChanged(IReadOnlyList<SnowportId> ids) =>
        EmitSignal(SignalName.DataSetsChanged, ids.Select(i => i.AsLong).ToArray());
}
