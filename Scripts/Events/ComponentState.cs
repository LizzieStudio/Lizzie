using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// A component's full replicated state. Every component write carries all of it.
/// </summary>
[JsonName("ComponentState")]
public record ComponentState : Atom
{
    public static readonly Id<ComponentState, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    public static readonly All<ComponentState> All = new();

    /// <summary>
    /// The components in each container, by <see cref="ContainerRef"/>. <see cref="SnowTag.Empty"/> holds the ones on the table.
    /// </summary>
    public static readonly Group<ComponentState, SnowTag> ByContainer = new(s => s.ContainerRef);

    /// <summary>
    /// The components on the table at each exact spot, such as a deck and the cards stacked on it.
    /// </summary>
    public static readonly Group<ComponentState, (int X, int Z)?> OnTableAt = new(s =>
        s.IsOnTable ? (s.X, s.Z) : null
    );

    /// <summary>
    /// The state last written for a component.
    /// </summary>
    public static ComponentState Of(VisualComponentBase c) =>
        c.State
        ?? throw new InvalidOperationException($"Component {c.Reference} has no written state.");

    /// <summary>
    /// Captures a node that hasn't been written yet, such as a spawn preview.
    /// </summary>
    public static ComponentState Capture(VisualComponentBase c) =>
        new()
        {
            Id = c.Reference,
            PrototypeRef = c.PrototypeRef,
            Position = c.Position,
            Rotation = c.Rotation,
            Card = c.Card,
        };

    public SnowTag PrototypeRef { get; init; }

    /// <summary>
    /// X relative to either the table or player cursor, in tenths of a millimeter.
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Z relative to either the table or player cursor, in tenths of a millimeter.
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

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PrototypeItemRef Card { get; init; } = PrototypeItemRef.None;

    /// <summary>
    /// What holds this component:
    /// <list type="bullet">
    /// <item><see cref="SnowTag.Empty"/> when it's on the table</item>
    /// <item>a component Id, for bags</item>
    /// <item>a seat's hand Id, for a player hand</item>
    /// <item>a seat's cursor Id, when being dragged by a player</item>
    /// </list>
    /// </summary>
    public SnowTag ContainerRef { get; init; } = SnowTag.Empty;

    /// <summary>
    /// True if it's sitting on the table, outside a component, and not being dragged.
    /// </summary>
    [JsonIgnore]
    public bool IsOnTable => ContainerRef == SnowTag.Empty;

    /// <summary>
    /// Whether it's one of its prototype's printed items.
    ///
    /// <list type="bullet">
    /// <item>a deck's card</item>
    /// <item>a single printed component</item>
    /// <item>a die spawned for a dataset row</item>
    /// </list>
    /// </summary>
    [JsonIgnore]
    public bool IsItem => Card != PrototypeItemRef.None;

    /// <summary>
    /// A deck's cards, or the components whose <see cref="ContainerRef"/> is this one, like a bag's or a tray's.
    /// </summary>
    /// <remarks>
    /// Any component used as a <see cref="ContainerRef"/> gets its contents here without changes.
    /// </remarks>
    public override IEnumerable<AtomRef> Contents(IAtomReader R)
    {
        var containedComponents =
            R.Kind(this) == VisualComponentBase.VisualComponentType.Deck
                ? R.TokensOn(this)
                : R.Get(ByContainer, Id);

        return containedComponents.Select(c => ById.Ref(c.Id));
    }

    /// <summary>
    /// The deck it's on or the bag it's in.
    /// </summary>
    /// <remarks>
    /// Any component referenced by <see cref="ContainerRef"/> becomes a container here without changes.
    /// </remarks>
    public override IEnumerable<AtomRef> Containers(IAtomReader R)
    {
        // A hand or a cursor isn't a component, so it isn't found.
        if (!IsOnTable)
            return R.Get(ById, ContainerRef) is { } container ? [ById.Ref(container.Id)] : [];

        if (R.Kind(this) != VisualComponentBase.VisualComponentType.Token)
            return [];

        return R.Get(OnTableAt, (X, Z))
            .Where(d =>
                d.ZOrder < ZOrder && R.Kind(d) == VisualComponentBase.VisualComponentType.Deck
            )
            .Select(d => ById.Ref(d.Id));
    }

    /// <summary>
    /// Its prototype and, for a card made from a dataset, its rows.
    /// </summary>
    /// <remarks>
    /// This will need to be extended as new references are invented.
    /// </remarks>
    public override IEnumerable<AtomRef> Referenced(IAtomReader R) =>
        new[]
        {
            Prototype.ById.Ref(PrototypeRef),
            DataRow.ById.Ref(Card.FrontRow),
            DataRow.ById.Ref(Card.BackRow),
        }.Where(r => (SnowTag)r.Id != SnowTag.Empty);
}
