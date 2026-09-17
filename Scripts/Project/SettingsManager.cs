using System.Linq;
using Godot;

/// <summary>
/// Tracks and syncs the project settings.
/// </summary>
public partial class SettingsManager : Node
{
    private static SettingsManager _instance;
    public static SettingsManager Instance => _instance;

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

    /// <summary>Merges the settings effect in the event, if any, into the project.</summary>
    private void OnEventApplied(TableEvent e)
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return;

        var fx = e.Effects.OfType<UpdateSettingsEffect>().LastOrDefault();
        if (fx?.Payload == null)
            return;

        if (e.Id.CompareTo(project.GameSettings.LastUpdateId) < 0)
            return;

        project.GameSettings = fx.Payload with { LastUpdateId = e.Id };
        EventBus.Instance.Publish<ProjectSettingsChangedEvent>();
    }
}
