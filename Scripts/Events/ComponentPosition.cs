using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// Where a component is in one <see cref="Stage"/>.
/// </summary>
[JsonName("ComponentPosition")]
public record ComponentPosition : StagedAtom
{
    public static readonly StagedId<ComponentPosition> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// The components each stage shows.
    /// </summary>
    public static readonly Group<ComponentPosition, SnowTag> ByStage = new(s => s.Stage);

    /// <summary>
    /// The components in each container of a stage, by <see cref="ContainerRef"/>.
    /// <see cref="SnowTag.Empty"/> holds the ones on the table.
    /// </summary>
    public static readonly Group<ComponentPosition, StageKey> ByContainer = new(s =>
        s.In(s.ContainerRef)
    );

    /// <summary>
    /// The components each stage wrote itself, rather than inherited.
    /// </summary>
    public static readonly Written<ComponentPosition> WrittenIn = new();

    /// <summary>
    /// The key of the component <paramref name="id"/> in this component's stage.
    /// </summary>
    /// <remarks>
    /// This is useful when dealing with containers, since you can get the contents in the container's stage.
    /// </remarks>
    public StageKey In(SnowTag id) => new(Stage, id);

    /// <summary>
    /// The state last written for a component.
    /// </summary>
    public static ComponentPosition Of(VisualComponentBase c) =>
        c.State
        ?? throw new InvalidOperationException($"Component {c.Reference} has no written state.");

    /// <summary>
    /// Captures a node that hasn't been written yet, such as a spawn preview, as <paramref name="variant"/>.
    /// </summary>
    public static ComponentPosition Capture(VisualComponentBase c, ComponentVariant variant) =>
        new()
        {
            Id = c.Reference,
            Stage = c.Key.Stage,
            VariantRef = variant.Id,
            Position = c.Position,
            Rotation = c.Rotation,
        };

    /// <summary>
    /// What this component is.
    /// </summary>
    public SnowTag VariantRef { get; init; }

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
    /// The components whose <see cref="ContainerRef"/> is this one.
    /// </summary>
    /// <remarks>
    /// Any component used as a <see cref="ContainerRef"/> gets its contents here without changes.
    /// </remarks>
    public override IEnumerable<AtomRef> Contents(IAtomReader R) =>
        R.Get(ByContainer, In(Id)).Select(AtomRef.To);

    /// <summary>
    /// The deck or bag it's in.
    /// </summary>
    /// <remarks>
    /// Any component referenced by <see cref="ContainerRef"/> becomes a container here without changes.
    /// </remarks>
    public override IEnumerable<AtomRef> Containers(IAtomReader R) =>
        // A hand or a cursor isn't a component, so it isn't found.
        !IsOnTable && R.Get(ById, In(ContainerRef)) is { } container
            ? [AtomRef.To(container)]
            : [];

    /// <summary>
    /// What its <see cref="ComponentVariant"/> refers to.
    /// </summary>
    public override IEnumerable<AtomRef> Referenced(IAtomReader R) => R.GetVariant(this).Referenced(R);
}
