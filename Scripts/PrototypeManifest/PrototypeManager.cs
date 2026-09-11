using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks prototype definitions as a last-write-wins register.
/// </summary>
public partial class PrototypeManager : ReplicatedStore<Prototype>
{
    private static PrototypeManager _instance;
    public static PrototypeManager Instance => _instance;

    [Signal]
    public delegate void PrototypesChangedEventHandler(long[] ids);

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

    protected override IDictionary<SnowportId, Prototype> Store =>
        ProjectService.Instance?.CurrentProject?.Prototypes;

    protected override void NotifyChanged(IReadOnlyList<SnowportId> ids) =>
        EmitSignal(SignalName.PrototypesChanged, ids.Select(i => i.AsLong).ToArray());
}
