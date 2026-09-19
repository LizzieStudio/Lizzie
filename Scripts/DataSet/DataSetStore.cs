using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks dataset definitions as a last-write-wins register.
/// </summary>
public partial class DataSetStore : ReplicatedStore<DataSet>
{
    private static DataSetStore _instance;
    public static DataSetStore Instance => _instance;

    [Signal]
    public delegate void DataSetsChangedEventHandler(int[] ids);

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

    protected override IDictionary<SnowTag, DataSet> Store =>
        ProjectService.Instance?.CurrentProject?.Datasets;

    protected override void NotifyChanged(IReadOnlyList<SnowTag> ids) =>
        EmitSignal(SignalName.DataSetsChanged, SnowTag.ToValues(ids));
}
