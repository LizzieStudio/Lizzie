using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

namespace Lizzie;

/// <summary>
/// Where a component is in one <see cref="Stage"/>.
/// The <see cref="Component"/> with its shared tag defines the component.
/// </summary>
[JsonName("ComponentPosition")]
public record ComponentPosition : StagedAtom
{
    public static readonly StagedId<ComponentPosition> ById = new(r => r.Id);

    /// <summary>
    /// The <see cref="Component"/> this is the position of, which is the same in every stage.
    /// Create one with <see cref="Ref{T}.Create"/> for the position and the <see cref="Component"/> both.
    /// </summary>
    public required Ref<Component> Id { get; init; }

    /// <summary>
    /// The components each stage shows.
    /// </summary>
    public static readonly Lookup<ComponentPosition, Ref<Stage>> ByStage = new(s => s.StageRef);

    /// <summary>
    /// The components in each container of a stage, by <see cref="ContainerRef"/>.
    /// <see cref="SnowTag.Empty"/> holds the ones on the table.
    /// </summary>
    public static readonly Lookup<ComponentPosition, StageKey<ComponentPosition>> ByContainer = new(
        s => s.ContainerKey
    );

    /// <summary>
    /// The components each stage wrote itself, rather than inherited.
    /// </summary>
    public static readonly Written<ComponentPosition> WrittenIn = new();

    /// <summary>
    /// The key of <paramref name="component"/> in this component's stage.
    /// </summary>
    /// <remarks>
    /// This is useful when dealing with containers, since you can get the contents in the container's stage.
    /// </remarks>
    public StageKey<ComponentPosition> In(Ref<Component> component) => new(StageRef, component);

    /// <summary>
    /// Whether resetting its stage would delete the component in <paramref name="key"/>.
    /// </summary>
    public static bool GoesOnReset(IAtomReader R, StageKey<ComponentPosition> key) =>
        R.Get(Stage.ById, key.StageRef) is { ParentRef: var parent }
        && !parent.IsEmpty
        && R.Get(ById, key with { StageRef = parent }) == null;

    /// <summary>
    /// The state last written for a component.
    /// </summary>
    public static ComponentPosition Of(VisualComponentBase c) =>
        c.State
        ?? throw new InvalidOperationException($"Component {c.ComponentRef} has no written state.");

    /// <summary>
    /// X in its container's frame, in tenths of a millimeter.
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Z in its container's frame, in tenths of a millimeter.
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

    public ZOrder ZOrder { get; init; }

    /// <summary>
    /// What holds this component:
    /// <list type="bullet">
    /// <item><see cref="SnowTag.Empty"/> when it's on the table</item>
    /// <item>a component Id, for decks and bags</item>
    /// <item>a seat's hand Id, for a player hand</item>
    /// <item>a seat's cursor Id, when being dragged by a player</item>
    /// </list>
    /// </summary>
    public SnowTag ContainerRef { get; init; } = SnowTag.Empty;

    /// <summary>
    /// True if it's sitting on the table, outside a container, and not being dragged.
    /// </summary>
    [JsonIgnore]
    public bool IsOnTable => ContainerRef == SnowTag.Empty;

    /// <summary>
    /// Its container's key in its stage, which <see cref="ByContainer"/> lists it under.
    /// A hand or a cursor isn't a component, so its tag stands in for one here.
    /// </summary>
    [JsonIgnore]
    public StageKey<ComponentPosition> ContainerKey => In(new(ContainerRef));

    /// <summary>
    /// The components whose <see cref="ContainerRef"/> is this one.
    /// </summary>
    /// <remarks>
    /// Any component used as a <see cref="ContainerRef"/> gets its contents here without changes.
    /// </remarks>
    public override IEnumerable<IAtomRef> Contents(IAtomReader R) =>
        R.Get(ByContainer, In(Id)).Select(IAtomRef.To);

    /// <summary>
    /// The deck or bag it's in.
    /// </summary>
    /// <remarks>
    /// Any component referenced by <see cref="ContainerRef"/> becomes a container here without changes.
    /// </remarks>
    public override IEnumerable<IAtomRef> Containers(IAtomReader R) =>
        // A hand or a cursor isn't a component, so it isn't found.
        !IsOnTable && R.Get(ById, ContainerKey) is { } container
            ? [IAtomRef.To(container)]
            : [];

    /// <summary>
    /// What its <see cref="Component"/> refers to.
    /// </summary>
    public override IEnumerable<IAtomRef> Referenced(IAtomReader R) =>
        R.GetComponent(this).Referenced(R);
}
