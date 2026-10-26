using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Holds a node for every component atom one stage shows.
/// Creates and removes the nodes, who then render their atom.
/// </summary>
public partial class StageView : Node3D
{
    public TextureFactory TextureFactory { get; set; }

    /// <summary>
    /// The stage whose components it shows.
    /// </summary>
    public Ref<Stage> StageRef { get; init; }

    private readonly Dictionary<SnowTag, VisualComponentBase> _components = new();

    // Cached here to prevent reading it from Godot, which got expensive.
    private readonly List<VisualComponentBase> _nodes = new();

    /// <summary>
    /// Every component node in the view, including those inside decks and bags.
    /// </summary>
    public IReadOnlyList<VisualComponentBase> Nodes => _nodes;

    /// <summary>
    /// Called when a component is added, removed, or synced.
    /// </summary>
    public event Action Changed;

    /// <summary>Called after a component is removed from the view.</summary>
    public event Action<VisualComponentBase> ComponentRemoved;

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    /// <summary>The node for a component atom, or null.</summary>
    public VisualComponentBase GetComponent(SnowTag reference) =>
        _components.GetValueOrDefault(reference);

    /// <summary>Called by components after they apply their atom.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    // The scene a component's node is made from, and what its node can't change without being remade.
    private static readonly Basis<
        ComponentPosition,
        (string Scene, Component Component)
    > NodeBasis = new(
        (R, s) =>
        {
            var component = R.GetComponent(s);
            var proto = R.Get(Prototype.ById, component.PrototypeRef);
            if (proto == null)
                return null;
            var scene = Utility.ComponentTypeToScenePath(
                proto.Type,
                proto.Parameters,
                R,
                component.IsItem
            );
            return (scene, component);
        }
    );

    /// <summary>
    /// Keeps one node per live component,
    /// either on the table or in its container.
    /// </summary>
    private void Sync(IWatchReader R)
    {
        var (removed, added) = R.Diff(ComponentPosition.ByStage, StageRef, NodeBasis);

        foreach (var (s, _) in removed)
            RemoveComponent(s.Id);
        foreach (var (s, key) in added)
            AddComponent(s.Id, key.Scene);

        foreach (var s in R.Get(ComponentPosition.ByStage, StageRef))
        {
            if (!_components.TryGetValue(s.Id, out var node))
                continue;
            var parent = _components.GetValueOrDefault(s.ContainerRef) ?? (Node)this;
            if (node.GetParent() != parent)
                node.Reparent(parent);
        }

        Changed?.Invoke();
    }

    private void RemoveComponent(SnowTag id)
    {
        if (!_components.Remove(id, out var component))
            return;

        foreach (var contained in component.GetChildren().OfType<VisualComponentBase>())
            contained.Reparent(this);
        component.GetParent().RemoveChild(component);
        component.QueueFree();
        _nodes.Remove(component);
        ComponentRemoved?.Invoke(component);
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

        component.ComponentRef = id;
        component.TextureFactory = TextureFactory;
        AddChild(component);
        _components[id] = component;
        _nodes.Add(component);
    }
}
