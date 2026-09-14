using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The effects of a TableAction.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@")]
[JsonDerivedType(typeof(ComponentEffect), "c")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Prototype>), "p")]
[JsonDerivedType(typeof(UpdatePlayerEffect), "u")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Template>), "tu")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<DataSet>), "du")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Lizzie.AssetManagement.Asset>), "a")]
[JsonDerivedType(typeof(UpdateSettingsEffect), "gs")]
public abstract class Effect
{
    /// <summary>The component or prototype this effect applies to.</summary>
    [JsonPropertyName("i")]
    public SnowTag Id { get; set; }
}

/// <summary>
/// A component upsert for creating, moving, reordering, and deleting a component.
/// </summary>
public class ComponentEffect : Effect
{
    [JsonPropertyName("p")]
    public SnowTag PrototypeRef { get; set; }

    [JsonPropertyName("s")]
    public VcSyncDto State { get; set; }

    /// <summary>
    /// Captures a component's state.
    /// </summary>
    public static ComponentEffect Capture(VisualComponentBase component) =>
        new()
        {
            Id = component.Reference,
            PrototypeRef = component.PrototypeRef,
            State = VcSyncDto.CaptureLive(component),
        };
}

/// <summary>
/// Creates, updates, or reversibly deletes a replicated definition.
/// </summary>
public class UpdateReplicatedEffect<T> : Effect
    where T : class, IReplicated
{
    [JsonPropertyName("v")]
    public T Payload { get; set; }
}

/// <summary>
/// Creates or updates the project settings.
/// </summary>
public class UpdateSettingsEffect : Effect
{
    [JsonPropertyName("v")]
    public ProjectGameSettings Payload { get; set; }
}

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
    public SnowTag HandRef { get; set; }

    /// <summary>The container id for this player's cursor.</summary>
    [JsonPropertyName("c")]
    public SnowTag CursorRef { get; set; }

    /// <summary>True once the player has left.</summary>
    [JsonPropertyName("l")]
    public bool HasLeft { get; set; }
}

