using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The effects of a TableAction.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@")]
[JsonDerivedType(typeof(ComponentEffect), "c")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Prototype>), "p")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Template>), "tu")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<DataSet>), "du")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<DataRow>), "dw")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Lizzie.AssetManagement.Asset>), "a")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<GameState>), "sv")]
[JsonDerivedType(typeof(SetReplicatedValueEffect<ProjectGameSettings>), "gs")]
[JsonDerivedType(typeof(TableClearEffect), "clr")]
[JsonDerivedType(typeof(SetReplicatedValueEffect<ActiveGameStateRef>), "ags")]
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
/// Sets a project-wide singleton value.
/// </summary>
public class SetReplicatedValueEffect<T> : Effect
{
    [JsonPropertyName("v")]
    public T Payload { get; set; }
}

/// <summary>
/// Removes every component older than the enclosing event's id.
/// Upserts from the same event "survive" the clear.
/// This is used when restoring from a snapshots.
/// </summary>
public class TableClearEffect : Effect { }
