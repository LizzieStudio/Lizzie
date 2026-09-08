using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The effects of a TableAction.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@")]
[JsonDerivedType(typeof(CreateEffect), "c")]
[JsonDerivedType(typeof(DeleteEffect), "d")]
[JsonDerivedType(typeof(TransformEffect), "t")]
[JsonDerivedType(typeof(PrototypeEffect), "p")]
[JsonDerivedType(typeof(PrototypeDeleteEffect), "pd")]
[JsonDerivedType(typeof(UpdatePlayerEffect), "u")]
[JsonDerivedType(typeof(UpdateTemplateEffect), "tu")]
[JsonDerivedType(typeof(UpdateDataSetEffect), "du")]
public abstract class Effect
{
    /// <summary>The component or prototype this effect applies to.</summary>
    [JsonPropertyName("i")]
    public SnowportId Id { get; set; }
}

/// <summary>
/// Brings a new component into existence.
/// </summary>
public class CreateEffect : Effect
{
    [JsonPropertyName("p")]
    public SnowportId PrototypeRef { get; set; }

    [JsonPropertyName("s")]
    public VcSyncDto State { get; set; }
}

/// <summary>Removes a component. Self-explanatory, so its parent event carries no action.</summary>
public class DeleteEffect : Effect { }

/// <summary>
/// Creates or updates a prototype definition.
/// </summary>
public class PrototypeEffect : Effect
{
    [JsonPropertyName("p")]
    public Prototype Prototype { get; set; }
}

/// <summary>Deletes a prototype definition.</summary>
public class PrototypeDeleteEffect : Effect { }

/// <summary>
/// Announces or updates a player.
/// </summary>
public class UpdatePlayerEffect : Effect
{
    /// <summary>The seat this player occupies.</summary>
    [JsonPropertyName("s")]
    public int Seat { get; set; }

    /// <summary>The container id for this player's hand.</summary>
    [JsonPropertyName("h")]
    public SnowportId HandRef { get; set; }

    /// <summary>The container id for this player's cursor.</summary>
    [JsonPropertyName("c")]
    public SnowportId CursorRef { get; set; }

    /// <summary>True once the player has left.</summary>
    [JsonPropertyName("l")]
    public bool HasLeft { get; set; }
}

/// <summary>
/// Creates, updates, or reversibly deletes a template definition.
/// </summary>
public class UpdateTemplateEffect : Effect
{
    [JsonPropertyName("t")]
    public Template Template { get; set; }
}

/// <summary>
/// Creates, updates, or reversibly deletes a dataset definition.
/// </summary>
public class UpdateDataSetEffect : Effect
{
    [JsonPropertyName("d")]
    public DataSet DataSet { get; set; }
}

/// <summary>
/// Instant move, reorientation, relocation, or reordering of a component.
/// </summary>
public class TransformEffect : Effect
{
    [JsonPropertyName("l")]
    public VisualComponentBase.ComponentLocation Location { get; set; }

    /// <summary>
    /// The container that holds the component or <see cref="SnowportId.Empty"/>.
    /// </summary>
    [JsonPropertyName("c")]
    public SnowportId ContainerRef { get; set; }

    [JsonPropertyName("p")]
    public Vector3 Position { get; set; }

    /// <summary>Target rotation in radians.</summary>
    [JsonPropertyName("r")]
    public Vector3 Rotation { get; set; }

    /// <summary>Where the effect sends the component in the ZOrder, or Unset to leave it.</summary>
    [JsonPropertyName("z")]
    public ZTarget ZTarget { get; set; }

    /// <summary>Separates components reordered by the same event. Higher ends up on top.</summary>
    [JsonPropertyName("zs")]
    public int ZSuborder { get; set; }

    /// <summary>
    /// Captures a component's current transform state.
    /// </summary>
    public static TransformEffect Capture(VisualComponentBase component) =>
        new()
        {
            Id = component.Reference,
            Location = component.Location,
            ContainerRef = component.ContainerRef,
            Position =
                component.Location == VisualComponentBase.ComponentLocation.Cursor
                    ? component.CursorOffset
                    : component.Position,
            Rotation = component.Rotation,
        };
}
