using System;
using System.Linq;
using Godot;

/// <summary>
/// Tracks <see cref="UpdateDataSetEffect"/>s as a dataset collection.
/// </summary>
public partial class DataSetManager : Node
{
    private static DataSetManager _instance;
    public static DataSetManager Instance => _instance;

    [Signal]
    public delegate void DataSetsChangedEventHandler();

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

    /// <summary>Captures the live datasets as upsert effects for a late joiner.</summary>
    public Effect[] GenerateCatchupEffects()
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return Array.Empty<Effect>();

        return project
            .Datasets.Values.Where(d => !d.Deleted)
            .Select(d => (Effect)new UpdateDataSetEffect { Id = d.DatasetRef, DataSet = d })
            .ToArray();
    }

    private void OnEventApplied(TableEvent e)
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return;

        bool changed = false;

        foreach (var effect in e.Effects)
        {
            if (effect is not UpdateDataSetEffect ud || ud.DataSet == null)
                continue;

            if (
                project.Datasets.TryGetValue(ud.Id, out var current)
                && e.Id.CompareTo(current.LastUpdateId) < 0
            )
                continue;

            var dataset = ud.DataSet;
            dataset.DatasetRef = ud.Id;
            dataset.LastUpdateId = e.Id;
            project.Datasets[ud.Id] = dataset;
            changed = true;
        }

        if (!changed)
            return;

        // Drive local re-render of any components built from these datasets.
        EventBus.Instance?.Publish<DataSetChangedEvent>();
        EmitSignal(SignalName.DataSetsChanged);
    }
}
