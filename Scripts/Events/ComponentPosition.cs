using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

namespace Lizzie;

/// <summary>
/// Where a component is in one <see cref="Stage"/>.
/// The <see cref="Component"/> with its shared tag defines the component.
///
/// <para>
/// <see cref="Component"/> holds the immutable properties of a component, like its shape or color,
/// whereas <see cref="ComponentPosition"/> holds the mutable properties, like its position or rotation.
/// As such, a blueprint and a table can both hold their own <see cref="ComponentPosition"/> for the same component.
/// While a normal <see cref="Atom"/> changes everywhere, a <see cref="ComponentPosition"/> changes only in its stage.
/// </para>
///
/// <para>
/// You read a position from a stage, but positions are inherited from the stage's ancestor blueprints,
/// meaning that you will actually get the most "recent" position available.
/// The position you get, however, shows the stage you read it from, not its origin, meaning you can write it immediately using <c>with { ... }</c>.
/// </para>
/// </summary>
[JsonName("ComponentPosition")]
public record ComponentPosition : Atom, IRanked
{
    public static readonly Identity<ComponentPosition, PositionKey> ById = new(r =>
        new(r.StageRef, r.Id)
    );

    /// <summary>
    /// The <see cref="Component"/> this is the position of, which is the same in every stage.
    /// Create one with <see cref="Ref{T}.Create"/> for the position and the <see cref="Component"/> both.
    /// </summary>
    public required Ref<Component> Id { get; init; }

    /// <summary>
    /// The stage this position was written in or read from.
    /// </summary>
    public Ref<Stage> StageRef { get; init; }

    /// <summary>
    /// True if this component was removed from the stage.
    /// Removed components are only returned by <see cref="WrittenIn"/>.
    /// </summary>
    /// <remarks>
    /// Make a removed position with <see cref="Removal"/>.
    /// To reset a position to the parent stage's instead, <see cref="AtomReaderExtensions.Unset"/> it.
    /// </remarks>
    public bool Removed { get; init; }

    /// <summary>
    /// A write which will remove the component from the stage, like a tombstone.
    /// </summary>
    public ComponentPosition Removal() =>
        new()
        {
            Id = Id,
            StageRef = StageRef,
            Removed = true,
        };

    /// <summary>
    /// The components each stage shows.
    /// </summary>
    public static readonly Lookup<ComponentPosition, Ref<Stage>> ByStage = new(s => s.StageRef);

    /// <summary>
    /// The components at each location of a stage, from the bottom of the stack up.
    /// A null location holds the ones on the table.
    /// </summary>
    public static readonly Lookup<ComponentPosition, LocationKey> ByLocation = new(
        s => new(s.StageRef, s.Location),
        IRanked.ByRank
    );

    /// <summary>
    /// The components each stage wrote itself, rather than inherited.
    /// </summary>
    public static readonly Written WrittenIn = new();

    /// <summary>
    /// The key of <paramref name="component"/> in this position's stage.
    /// </summary>
    /// <remarks>
    /// This is useful when dealing with containers, since you can get the contents in the container's stage.
    /// </remarks>
    public PositionKey KeyOf(Ref<Component> component) => new(StageRef, component);

    /// <summary>
    /// Whether resetting its stage would delete the component in <paramref name="key"/>.
    /// </summary>
    public static bool GoesOnReset(IAtomReader R, PositionKey key)
    {
        if (R.Get(Stage.ById, key.StageRef) is { ParentRef: { IsEmpty: false } parent })
        {
            var inParent = R.Get(ById, key with { StageRef = parent });
            return inParent == null || R.IsRemoved(inParent);
        }

        return false;
    }

    /// <summary>
    /// The state last written for a component.
    /// </summary>
    public static ComponentPosition Of(VisualComponentBase c) =>
        c.State
        ?? throw new InvalidOperationException($"Component {c.ComponentRef} has no written state.");

    /// <summary>
    /// X in its location's frame, in tenths of a millimeter.
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Z in its location's frame, in tenths of a millimeter.
    /// </summary>
    public int Z { get; init; }

    /// <summary>
    /// Sets <see cref="X"/> and <see cref="Z"/> from a node position, rounding to the nearest
    /// tenth of a millimeter. Y is dropped since stacking derives height.
    /// </summary>
    [JsonIgnore]
    public Vector3 Position
    {
        init
        {
            X = CmToTenthMm(value.X);
            Z = CmToTenthMm(value.Z);
        }
    }

    /// <summary>
    /// The node position for this state with the provided <paramref name="y"/>.
    /// </summary>
    public Vector3 PositionAt(float y) => new(TenthMmToCm(X), y, TenthMmToCm(Z));

    /// <summary>
    /// A node's table position in tenths of a millimeter.
    /// </summary>
    public static (int X, int Z) TableKey(Vector3 nodePosition) =>
        (CmToTenthMm(nodePosition.X), CmToTenthMm(nodePosition.Z));

    // Node units are centimeters.
    private const float TenthMmPerUnit = 100f;

    private static int CmToTenthMm(float v) => (int)MathF.Round(v * TenthMmPerUnit);

    private static float TenthMmToCm(int v) => v / TenthMmPerUnit;

    public Vector3 Rotation { get; init; }

    /// <summary>
    /// True if it shows its back.
    /// </summary>
    /// <remarks>
    /// This works whether or not it's on the table or in a hand.
    /// In a hand, it's true if it's face down to the holding player.
    /// </remarks>
    [JsonIgnore]
    public bool IsFaceDown => Mathf.Cos(Rotation.Z) < 0;

    /// <summary>
    /// Orders it among the components at the same location.
    /// </summary>
    public SnowRank Rank { get; init; }

    /// <summary>
    /// What entity this component is in, like a deck, a hand, a player's cursor, or null if it's out on the table.
    /// </summary>
    public Location Location { get; init; }

    /// <summary>
    /// True if it's sitting on the table, outside a container, and not being dragged.
    /// </summary>
    [JsonIgnore]
    public bool IsOnTable => Location == null;

    /// <summary>
    /// True while a player drags it.
    /// </summary>
    [JsonIgnore]
    public bool IsDragged => Location is Dragged;

    /// <summary>
    /// The container it's inside, like a deck or a bag, or Empty.
    /// </summary>
    [JsonIgnore]
    public Ref<Component> ContainerRef =>
        Location is InComponent { Component: var container } ? container : Ref<Component>.Empty;

    /// <summary>
    /// The key used with <see cref="ByLocation"/> to list this component's contents.
    /// </summary>
    [JsonIgnore]
    public LocationKey ContentsKey => LocationKey.Inside(KeyOf(Id));

    /// <summary>
    /// The components in this one.
    /// </summary>
    /// <remarks>
    /// Any component used as an <see cref="InComponent"/> gets its contents here without changes.
    /// </remarks>
    public override IEnumerable<IAtomRef> Contents(IAtomReader R) =>
        R.Get(ByLocation, ContentsKey).Select(IAtomRef.To);

    /// <summary>
    /// The deck or bag it's in.
    /// </summary>
    /// <remarks>
    /// Any component used as an <see cref="InComponent"/> becomes a container here without changes.
    /// </remarks>
    public override IEnumerable<IAtomRef> Containers(IAtomReader R)
    {
        if (R.Get(ById, KeyOf(ContainerRef)) is { } position)
            return [IAtomRef.To(position)];
        return [];
    }

    /// <summary>
    /// What its <see cref="Component"/> refers to.
    /// </summary>
    public override IEnumerable<IAtomRef> Referenced(IAtomReader R) =>
        R.GetComponent(this).Referenced(R);
}

/// <summary>
/// The id of a <see cref="ComponentPosition"/>.
/// </summary>
public readonly record struct PositionKey(Ref<Stage> StageRef, Ref<Component> ComponentRef)
    : IAtomRef<ComponentPosition>,
        IComparable<PositionKey>
{
    [JsonIgnore]
    public bool IsEmpty => StageRef.IsEmpty || ComponentRef.IsEmpty;

    public int CompareTo(PositionKey other) =>
        (StageRef, ComponentRef).CompareTo((other.StageRef, other.ComponentRef));
}
