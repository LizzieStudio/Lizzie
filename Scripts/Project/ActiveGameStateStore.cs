using System.Linq;
using Godot;

/// <summary>
/// Tracks and synchronizes the active-snapshot pointer.
/// </summary>
public partial class ActiveGameStateStore : Node
{
    private static ActiveGameStateStore _instance;
    public static ActiveGameStateStore Instance => _instance;

    /// <summary>Raised after the active snapshot changes.</summary>
    [Signal]
    public delegate void ActiveGameStateChangedEventHandler();

    /// <summary>The id of the last event that wrote <see cref="Project.ActiveGameState"/>.</summary>
    private SnowportId _activeWriteId = SnowportId.Empty;

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += HandleApplied;
    }

    public override void _ExitTree()
    {
        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied -= HandleApplied;

        if (_instance == this)
            _instance = null;
    }

    /// <summary>Sets the active-snapshot.</summary>
    private void HandleApplied(TableEvent e)
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return;

        if (e.Action is UndoAction)
        {
            RecomputeActive(project);
            return;
        }

        var fx = e.Effects.OfType<ActiveGameStateEffect>().LastOrDefault();
        if (fx == null || e.Id.CompareTo(_activeWriteId) < 0)
            return;

        _activeWriteId = e.Id;
        project.ActiveGameState = fx.Target;
        EmitSignal(SignalName.ActiveGameStateChanged);
    }

    /// <summary>
    /// Restores the active-snapshot pointer from the full log.
    /// </summary>
    public void RebuildActiveFromLog()
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project != null)
            RecomputeActive(project);
    }

    /// <summary>
    /// Restores the active-snapshot pointer to the latest non-undone switch or save.
    /// </summary>
    private void RecomputeActive(Project project)
    {
        var log = EventSynchronizer.Instance?.EventLog;
        if (log == null)
            return;

        var undone = UndoLog.ComputeUndone(log);
        var active = SnowTag.Empty;
        var writeId = SnowportId.Empty;

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var ev = log.GetAt(i).Value;
            if (undone.Contains(ev.Id))
                continue;

            var fx = ev.Effects.OfType<ActiveGameStateEffect>().LastOrDefault();
            if (fx != null)
            {
                active = fx.Target;
                writeId = ev.Id;
                break;
            }
        }

        _activeWriteId = writeId;
        project.ActiveGameState = active;
        EmitSignal(SignalName.ActiveGameStateChanged);
    }

    /// <summary>
    /// Resets the active-snapshot write tracker in preparation for a new game.
    /// </summary>
    public void ResetForJoin()
    {
        _activeWriteId = SnowportId.Empty;
    }
}
