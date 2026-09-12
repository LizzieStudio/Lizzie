using System;
using System.Collections.Generic;

/// <summary>
/// A serialisable snapshot of a single VisualComponent in the scene.
/// Extends VcSyncDto with the prototype reference and component
/// identity so the scene can be fully reconstructed.
/// </summary>
public class GameStateComponent : VcSyncDto
{
    public GameStateComponent() { }

    public GameStateComponent(VisualComponentBase component)
        : base(component)
    {
        ComponentRef = component.Reference;
        PrototypeRef = component.PrototypeRef;
    }

    /// <summary>Unique identity of this component instance.</summary>
    public SnowTag ComponentRef { get; set; }

    /// <summary>The prototype from which this component was built.</summary>
    public SnowTag PrototypeRef { get; set; }

    /// <summary>
    /// Populate this record from a live VisualComponent.
    /// </summary>
    public static GameStateComponent FromComponent(VisualComponentBase component) => new(component);
}

/// <summary>
/// A named snapshot of the complete scene — all VisualComponents present in
/// GameObjects at the moment of capture.
/// </summary>
public class GameState
{
    /// <summary>Human-readable name chosen by the user.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>UTC timestamp of when the snapshot was taken.</summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// A freeform description entered by the user describing what this is (optional)
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// One record per VisualComponent that was present (and visible) in the scene.
    /// </summary>
    public List<GameStateComponent> Components { get; set; } = new();
}
