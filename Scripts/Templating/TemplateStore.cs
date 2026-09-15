using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks template definitions as a last-write-wins register.
/// </summary>
public partial class TemplateStore : ReplicatedStore<Template>
{
    private static TemplateStore _instance;
    public static TemplateStore Instance => _instance;

    [Signal]
    public delegate void TemplatesChangedEventHandler(int[] ids);

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

    protected override IDictionary<SnowTag, Template> Store =>
        ProjectService.Instance?.CurrentProject?.Templates;

    protected override void NotifyChanged(IReadOnlyList<SnowTag> ids) =>
        EmitSignal(SignalName.TemplatesChanged, ids.Select(i => i.Value).ToArray());
}
