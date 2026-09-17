using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks and synchronizes saved game states and the active-snapshot pointer.
/// </summary>
public partial class GameStatesStore : ReplicatedStore<GameState>
{
    private static GameStatesStore _instance;
    public static GameStatesStore Instance => _instance;

    /// <summary>The id of the last event that wrote <see cref="Project.ActiveGameState"/>.</summary>
    private SnowportId _activeWriteId = SnowportId.Empty;

    /// <summary>
    /// Whether the table is in snapshot edit mode.
    /// </summary>
    public bool EditMode { get; private set; }

    private bool _capturePending;

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
        EventBus.Instance.Publish(new GameStateChangedEvent { Editing = EditMode });
    }

    /// <summary>Merges game-state records and sets the active-snapshot.</summary>
    private void HandleApplied(TableEvent e)
    {
        OnEventApplied(e);

        var project = ProjectService.Instance?.CurrentProject;
        if (project == null)
            return;

        MaybeScheduleCapture(e);

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
        SetEditModeFlag(fx.Editing);
    }

    /// <summary>Applies an edit-mode transition.</summary>
    private void SetEditModeFlag(bool editing)
    {
        bool was = EditMode;
        EditMode = editing;
        EventBus.Instance.Publish(new GameStateChangedEvent { Editing = EditMode });

        if (was && !editing)
            ProjectService.Instance?.SaveProject();
    }

    /// <summary>
    /// While in edit mode, re-derive the active snapshot from the table after any real table change.
    /// </summary>
    private void MaybeScheduleCapture(TableEvent e)
    {
        if (!EditMode)
            return;
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null || project.ActiveGameState == SnowTag.Empty)
            return;
        if (e.Action is GameStateSwitchAction)
            return;
        if (e.Effects.OfType<UpdateReplicatedEffect<GameState>>().Any())
            return;
        if (!(e.Action is UndoAction || e.Effects.OfType<ComponentEffect>().Any()))
            return;

        if (_capturePending)
            return;
        _capturePending = true;
        Callable.From(RunCapture).CallDeferred();
    }

    private void RunCapture()
    {
        _capturePending = false;
        if (EditMode)
            ProjectService.Instance?.CaptureActiveSnapshotLocal();
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
        var editing = false;

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
                editing = fx.Editing;
                break;
            }
        }

        _activeWriteId = writeId;
        project.ActiveGameState = active;
        SetEditModeFlag(editing);
    }

    /// <summary>
    /// Resets the active-snapshot write tracker in preparation for a new game.
    /// </summary>
    public void ResetForJoin()
    {
        _activeWriteId = SnowportId.Empty;
        EditMode = false;
    }

    /// <summary>Adds the active-snapshot pointer to the catchup for a joining client.</summary>
    public override Effect[] GenerateCatchupEffects()
    {
        var effects = base.GenerateCatchupEffects();
        var project = ProjectService.Instance?.CurrentProject;
        if (project == null || project.ActiveGameState == SnowTag.Empty)
            return effects;

        return effects
            .Append(
                new ActiveGameStateEffect { Target = project.ActiveGameState, Editing = EditMode }
            )
            .ToArray();
    }
}
