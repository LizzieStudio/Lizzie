using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The effects of a TableAction.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreateEffect), "Create")]
[JsonDerivedType(typeof(DeleteEffect), "Delete")]
[JsonDerivedType(typeof(TransformEffect), "Transform")]
[JsonDerivedType(typeof(RestructureEffect), "Restructure")]
public abstract class Effect
{
    /// <summary>The component this effect applies to.</summary>
    public SnowportId ComponentRef { get; set; }
}

/// <summary>
/// Brings a new component into existence.
/// </summary>
public class CreateEffect : Effect
{
    public Guid PrototypeRef { get; set; }

    public string ComponentName { get; set; } = string.Empty;

    public VcSyncDto State { get; set; }
}

/// <summary>Removes a component. Self-explanatory, so its parent event carries no action.</summary>
public class DeleteEffect : Effect { }

/// <summary>
/// Instant move, reorientation, relocation, or reordering of a component.
/// </summary>
public class TransformEffect : Effect
{
    public VisualComponentBase.ComponentLocation Location { get; set; }

    public Vector3 Position { get; set; }

    /// <summary>Target rotation in radians.</summary>
    public Vector3 Rotation { get; set; }

    /// <summary>Where the effect sends the component in the ZOrder, or Unset to leave it.</summary>
    public ZTarget ZTarget { get; set; }

    /// <summary>Separates components reordered by the same event. Higher ends up on top.</summary>
    public int ZSuborder { get; set; }

    /// <summary>
    /// Captures a component's current transform state.
    /// </summary>
    public static TransformEffect Capture(VisualComponentBase component) =>
        new()
        {
            ComponentRef = component.Reference,
            Location = component.Location,
            Position = component.Position,
            Rotation = component.Rotation,
        };
}

/// <summary>
/// Replaces a container's ordered child list.
/// </summary>
public class RestructureEffect : Effect
{
    public SnowportId[] Children { get; set; } = Array.Empty<SnowportId>();

    /// <summary>
    /// Captures a container's current ordered children.
    /// </summary>
    public static RestructureEffect Capture(VisualComponentGroup container) =>
        new() { ComponentRef = container.Reference, Children = container.GetContainerChildren() };
}
