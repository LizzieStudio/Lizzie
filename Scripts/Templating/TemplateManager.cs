using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks template definitions as a last-write-wins register.
/// </summary>
public partial class TemplateManager : ReplicatedStore<Template>
{
    private static TemplateManager _instance;
    public static TemplateManager Instance => _instance;

    [Signal]
    public delegate void TemplatesChangedEventHandler(long[] ids);

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

    protected override IDictionary<SnowportId, Template> Store =>
        ProjectService.Instance?.CurrentProject?.Templates;

    protected override void NotifyChanged(IReadOnlyList<SnowportId> ids) =>
        EmitSignal(SignalName.TemplatesChanged, ids.Select(i => i.AsLong).ToArray());
}
