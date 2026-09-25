using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Holds a node for every component record in the current game.
/// Creates and removes the nodes, who then render their record.
/// </summary>
public partial class Table : Node
{
    public TextureFactory TextureFactory { get; set; }

    private readonly Dictionary<SnowTag, VisualComponentBase> _components = new();

    /// <summary>
    /// Called when a component is added, removed, or synced.
    /// </summary>
    public event Action Changed;

    /// <summary>Called after a component is removed from the table.</summary>
    public event Action<VisualComponentBase> ComponentRemoved;

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    /// <summary>The node for a component record, or null.</summary>
    public VisualComponentBase GetComponent(SnowTag reference) =>
        _components.GetValueOrDefault(reference);

    /// <summary>Called by components after they apply their record.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>
    /// Keeps one node per live component whose prototype is avialable.
    /// </summary>
    private void Sync(IRecordReader R)
    {
        var (deleted, created) = R.GetChanged<
            ComponentState,
            (string Scene, SnowTag Proto, int Row, SnowTag RowId)
        >(
            (R, s) =>
            {
                var proto = R.GetIncludingDeleted<Prototype>(s.PrototypeRef);
                if (proto == null)
                    return null;
                var scene = Utility.ComponentTypeToScenePath(
                    proto.Type,
                    proto.Parameters,
                    s.DataSetRowIndex,
                    s.DataSetRowId
                );
                return (scene, s.PrototypeRef, s.DataSetRowIndex, s.DataSetRowId);
            }
        );

        foreach (var (id, _) in deleted)
            RemoveComponent(id);
        foreach (var (id, key) in created)
            AddComponent(id, key.Scene);

        Changed?.Invoke();
    }

    private void AddComponent(SnowTag id, string scenePath)
    {
        var scene = GD.Load<PackedScene>(scenePath).Instantiate();
        if (scene is not VisualComponentBase component)
        {
            GD.PrintErr($"Spawned scene for {id} is not a VisualComponentBase");
            scene.QueueFree();
            return;
        }

        component.Reference = id;
        component.TextureFactory = TextureFactory;
        AddChild(component);
        _components[id] = component;
    }

    private void RemoveComponent(SnowTag id)
    {
        if (!_components.Remove(id, out var component))
            return;
        RemoveChild(component);
        component.QueueFree();
        ComponentRemoved?.Invoke(component);
    }
}
