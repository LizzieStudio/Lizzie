using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TTSS.Scripts.Templating;

/// <summary>
/// Tracks <see cref="UpdateTemplateEffect"/>s as a template collection.
/// </summary>
public partial class TemplateManager : Node
{
    private static TemplateManager _instance;
    public static TemplateManager Instance => _instance;

    [Signal]
    public delegate void TemplatesChangedEventHandler();

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

    /// <summary>Captures the live templates as upsert effects for a late joiner.</summary>
    public Effect[] GenerateCatchupEffects()
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return Array.Empty<Effect>();

        return project
            .Templates.Values.Where(t => !t.Deleted)
            .Select(t => (Effect)new UpdateTemplateEffect { Id = t.TemplateRef, Template = t })
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
            if (effect is not UpdateTemplateEffect ut || ut.Template == null)
                continue;

            if (
                project.Templates.TryGetValue(ut.Id, out var current)
                && e.Id.CompareTo(current.LastUpdateId) < 0
            )
                continue;

            var template = ut.Template;
            template.TemplateRef = ut.Id;
            template.LastUpdateId = e.Id;
            project.Templates[ut.Id] = template;
            changed = true;
        }

        if (!changed)
            return;

        // Drive local re-render of any components built from these templates.
        EventBus.Instance?.Publish<TemplateChangedEvent>();
        EmitSignal(SignalName.TemplatesChanged);
    }
}
