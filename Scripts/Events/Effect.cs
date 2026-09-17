using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The effects of a TableAction.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@")]
[JsonDerivedType(typeof(ComponentEffect), "c")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Prototype>), "p")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Connection>), "cn")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Template>), "tu")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<DataSet>), "du")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Lizzie.AssetManagement.Asset>), "a")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<GameState>), "sv")]
[JsonDerivedType(typeof(UpdateSettingsEffect), "gs")]
[JsonDerivedType(typeof(TableClearEffect), "clr")]
[JsonDerivedType(typeof(ActiveGameStateEffect), "ags")]
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
/// Removes every component older than the enclosing event's id.
/// Upserts from the same event "survive" the clear.
/// This is used when restoring from a snapshots.
/// </summary>
public class TableClearEffect : Effect { }

/// <summary>
/// Sets which snapshot is the active (loaded) one.
/// </summary>
public class ActiveGameStateEffect : Effect
{
    [JsonPropertyName("t")]
    public SnowTag Target { get; set; }

    /// <summary>Whether switching to this snapshot also enters edit mode.</summary>
    [JsonPropertyName("ed")]
    public bool Editing { get; set; }
}

/// <summary>
/// Creates or updates the project settings.
/// </summary>
public class UpdateSettingsEffect : Effect
{
    [JsonPropertyName("v")]
    public ProjectGameSettings Payload { get; set; }
}

