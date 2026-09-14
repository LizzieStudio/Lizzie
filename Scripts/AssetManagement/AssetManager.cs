using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.AssetManagement;

/// <summary>
/// Tracks assets.
/// </summary>
public partial class AssetManager : ReplicatedStore<Asset>
{
    private static AssetManager _instance;
    public static AssetManager Instance => _instance;

    [Signal]
    public delegate void AssetsChangedEventHandler(int[] ids);

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

    protected override IDictionary<SnowTag, Asset> Store =>
        ProjectService.Instance?.CurrentProject?.Images;

    protected override void NotifyChanged(IReadOnlyList<SnowTag> ids)
    {
        EmitSignal(SignalName.AssetsChanged, ids.Select(i => i.Value).ToArray());

        var store = Store;
        foreach (var id in ids)
        {
            if (store != null && store.TryGetValue(id, out var asset))
                EventBus.Instance.Publish(new AssetChangedEvent { Asset = asset });
        }
    }
}
