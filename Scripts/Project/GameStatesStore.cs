using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Tracks and synchronizes saved game states and the active-snapshot pointer.
/// </summary>
public partial class GameStatesStore : ReplicatedStore<GameState>
{
    private static GameStatesStore _instance;
    public static GameStatesStore Instance => _instance;

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

    protected override IDictionary<SnowTag, GameState> Store =>
        ProjectService.Instance?.CurrentProject?.GameStates;

    protected override void NotifyChanged(IReadOnlyList<SnowTag> ids)
    {
        EventBus.Instance.Publish(new GameStateChangedEvent());
    }

    /// <summary>Merges game-state records and sets the active-snapshot.</summary>
    private void HandleApplied(TableEvent e)
    {
        OnEventApplied(e);

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
        EventBus.Instance.Publish(new GameStateChangedEvent());
    }

    /// <summary>
    /// Restores the active-snapshot pointer to the latest non-undone switch or save.
    /// </summary>
    private void RecomputeActive(Project project)
    {
        var log = EventSynchronizer.Instance?.Events;
        if (log == null)
            return;

        var undone = UndoLog.ComputeUndone(log);
        var active = SnowTag.Empty;
        var writeId = SnowportId.Empty;

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var ev = log[i];
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
        EventBus.Instance.Publish(new GameStateChangedEvent());
    }

    /// <summary>
    /// Clears snapshots in preparation for join.
    /// </summary>
    public void ResetForJoin()
    {
        Store?.Clear();
        _activeWriteId = SnowportId.Empty;

        var project = ProjectService.Instance?.CurrentProject;
        if (project != null)
            project.ActiveGameState = SnowTag.Empty;
    }

    /// <summary>Adds the active-snapshot pointer to the catchup for a joining client.</summary>
    public override Effect[] GenerateCatchupEffects()
    {
        var effects = base.GenerateCatchupEffects();
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null || project.ActiveGameState == SnowTag.Empty)
            return effects;

        return effects
            .Append(new ActiveGameStateEffect { Target = project.ActiveGameState })
            .ToArray();
    }
}
