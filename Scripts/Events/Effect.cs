using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The effects of a TableEvent.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(UpdateReplicatedEffect<ComponentState>), "ComponentState")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Prototype>), "Prototype")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Template>), "Template")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<DataSet>), "DataSet")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<DataRow>), "DataRow")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Lizzie.AssetManagement.Asset>), "Asset")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<GameState>), "GameState")]
[JsonDerivedType(typeof(UpdateReplicatedEffect<Selection>), "Selection")]
[JsonDerivedType(typeof(SetReplicatedValueEffect<ProjectGameSettings>), "ProjectGameSettings")]
[JsonDerivedType(typeof(SetReplicatedValueEffect<ActiveGameStateRef>), "ActiveGameStateRef")]
public abstract class Effect
{
    /// <summary>
    /// The record this effect writes.
    /// </summary>
    public SnowTag Id { get; set; }

    /// <summary>
    /// The type of record or value it writes, which names the store that holds it.
    /// </summary>
    internal abstract Type Writes { get; }

    /// <summary>
    /// A new, empty store for what it writes.
    /// </summary>
    internal abstract IReplicatedStore NewStore();

    /// <summary>
    /// Creates, updates, or reversibly deletes a record with its whole value.
    /// </summary>
    public static UpdateReplicatedEffect<T> Upsert<T>(T record)
        where T : class, IReplicated => new() { Id = record.Id, Payload = record };

    /// <summary>
    /// An upsert for each record.
    /// </summary>
    public static IEnumerable<Effect> UpsertAll<T>(IEnumerable<T> records)
        where T : class, IReplicated => records.Select(r => (Effect)Upsert(r));
}

/// <summary>
/// Creates, updates, or reversibly deletes a record.
/// </summary>
public class UpdateReplicatedEffect<T> : Effect
    where T : class, IReplicated
{
    public T Payload { get; set; }

    internal override Type Writes => typeof(T);

    internal override IReplicatedStore NewStore() => new ReplicatedDictionary<T>();
}

/// <summary>
/// Sets a project-wide singleton value.
/// </summary>
public class SetReplicatedValueEffect<T> : Effect
    where T : class, new()
{
    public T Payload { get; set; }

    internal override Type Writes => typeof(T);

    internal override IReplicatedStore NewStore() => new ReplicatedValue<T>();
}
