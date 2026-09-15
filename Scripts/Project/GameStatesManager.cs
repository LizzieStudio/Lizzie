using System.Collections.Generic;

/// <summary>
/// Tracks and synchronizes saved game states.
/// </summary>
public partial class GameStatesManager : ReplicatedStore<GameState>
{
    private static GameStatesManager _instance;
    public static GameStatesManager Instance => _instance;

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

    protected override IDictionary<SnowTag, GameState> Store =>
        ProjectService.Instance?.CurrentProject?.GameStates;

    protected override void NotifyChanged(IReadOnlyList<SnowTag> ids)
    {
        EventBus.Instance.Publish(new GameStateChangedEvent());
    }
}
