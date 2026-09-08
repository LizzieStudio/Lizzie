using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks <see cref="PrototypeEffect"/>s as a prototype collection.
/// </summary>
public partial class PrototypeManager : Node
{
    private static PrototypeManager _instance;
    public static PrototypeManager Instance => _instance;

    [Signal]
    public delegate void PrototypesChangedEventHandler();

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

    /// <summary>Captures the live prototypes as upsert effects for a late joiner.</summary>
    public Effect[] GenerateCatchupEffects()
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return Array.Empty<Effect>();

        return project
            .Prototypes.Values.Where(p => !p.Deleted)
            .Select(p => (Effect)new PrototypeEffect { Id = p.PrototypeRef, Prototype = p })
            .ToArray();
    }

    private void OnEventApplied(TableEvent e)
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return;

        var changedIds = new List<SnowportId>();

        foreach (var effect in e.Effects)
        {
            if (effect is not PrototypeEffect p || p.Prototype == null)
                continue;

            if (
                project.Prototypes.TryGetValue(p.Id, out var current)
                && e.Id.CompareTo(current.LastUpdateId) < 0
            )
                continue;

            var prototype = p.Prototype;
            prototype.PrototypeRef = p.Id;
            prototype.LastUpdateId = e.Id;
            project.Prototypes[p.Id] = prototype;
            changedIds.Add(p.Id);
        }

        if (changedIds.Count == 0)
            return;

        // Drive local re-render of any components built from these prototypes.
        foreach (var id in changedIds)
            EventBus.Instance?.Publish(new PrototypeChangedEvent { PrototypeId = id });

        EmitSignal(SignalName.PrototypesChanged);
    }
}
