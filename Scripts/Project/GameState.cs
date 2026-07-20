using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// A serialisable snapshot of a single VisualComponent in the scene.
/// Mirrors the data captured by VcSyncDto while also storing the prototype
/// reference and component identity so the scene can be fully reconstructed.
/// </summary>
public class GameStateComponent
{
    /// <summary>Unique identity of this component instance.</summary>
    public Guid ComponentRef { get; set; }

    /// <summary>The prototype from which this component was built.</summary>
    public Guid PrototypeRef { get; set; }

    /// <summary>Dataset row used for template-driven components (empty if none).</summary>
    public string DataSetRow { get; set; } = string.Empty;

    /// <summary>Display name at the time of capture.</summary>
    public string ComponentName { get; set; } = string.Empty;

    // Position stored as individual floats — Vector3 is not JSON-serialisable.
    public float Px { get; set; }
    public float Py { get; set; }
    public float Pz { get; set; }

    // Rotation (radians) stored as individual floats.
    public float Rx { get; set; }
    public float Ry { get; set; }
    public float Rz { get; set; }

    public bool LogicalVisible { get; set; } = true;

    public int ZOrder { get; set; }

    public VisualComponentBase.LayerType Layer { get; set; }

    public VisualComponentBase.ComponentLocation Location { get; set; }

    /// <summary>
    /// References to child components held inside a container/group (e.g. deck cards).
    /// </summary>
    public Guid[] ContainedComponents { get; set; } = Array.Empty<Guid>();

    // ── Convenience accessors (ignored by JSON serialiser) ──────────────────

    [JsonIgnore]
    public Vector3 Position
    {
        get => new(Px, Py, Pz);
        set
        {
            Px = value.X;
            Py = value.Y;
            Pz = value.Z;
        }
    }

    [JsonIgnore]
    public Vector3 Rotation
    {
        get => new(Rx, Ry, Rz);
        set
        {
            Rx = value.X;
            Ry = value.Y;
            Rz = value.Z;
        }
    }

    /// <summary>
    /// Populate this record from a live VisualComponent.
    /// </summary>
    public static GameStateComponent FromComponent(VisualComponentBase component)
    {
        var entry = new GameStateComponent
        {
            ComponentRef = component.Reference,
            PrototypeRef = component.PrototypeRef,
            DataSetRow = component.DataSetRow ?? string.Empty,
            ComponentName = component.ComponentName ?? string.Empty,
            Position = component.Position,
            Rotation = component.Rotation,
            LogicalVisible = component.LogicalVisible,
            ZOrder = component.ZOrder,
            Layer = component.Layer,
            Location = component.Location,
        };

        if (component is VisualComponentGroup group)
        {
            entry.ContainedComponents = group.GetContainerChildren();
        }

        return entry;
    }

    /// <summary>
    /// Apply the snapshot values back to a live VisualComponent.
    /// </summary>
    public void ApplyToComponent(VisualComponentBase component)
    {
        component.Position = Position;
        component.Rotation = Rotation;
        component.LogicalVisible = LogicalVisible;
        component.ZOrder = ZOrder;
        component.Layer = Layer;
        component.Location = Location;
        component.DataSetRow = DataSetRow;

        if (component is VisualComponentGroup group)
        {
            group.SetContainerChildren(ContainedComponents);
        }

        component.SyncRequired = false;
    }
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
