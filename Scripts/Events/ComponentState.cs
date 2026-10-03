using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// A component's full replicated state. Every component write carries all of it.
/// </summary>
public record ComponentState : Replicated
{
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
            DataSetRowIndex = c.DataSetRowIndex,
            DataSetRowId = c.DataSetRowId,
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
    public int DataSetRowIndex { get; init; } = -1;

    public SnowTag DataSetRowId { get; init; } = SnowTag.Empty;

    public VisualComponentBase.ComponentLocation Location { get; init; }

    /// <summary>
    /// The container that holds this component or <see cref="SnowTag.Empty"/>.
    /// </summary>
    public SnowTag ContainerRef { get; init; } = SnowTag.Empty;

    /// <summary>
    /// While <see cref="VisualComponentBase.ComponentLocation.Cursor"/>, the Snowport source of the
    /// player holding it.
    /// </summary>
    public byte Holder { get; init; }

    /// <summary>Whether a player's cursor is holding it.</summary>
    [JsonIgnore]
    public bool IsHeld => Location == VisualComponentBase.ComponentLocation.Cursor;

    /// <summary>Whether it's inside a container, such as a bag or a player's hand.</summary>
    [JsonIgnore]
    public bool IsContained => ContainerRef != SnowTag.Empty;

    /// <summary>Whether it's one of a deck's cards, rather than the deck itself.</summary>
    [JsonIgnore]
    public bool IsCard => DataSetRowIndex >= 0 || DataSetRowId != SnowTag.Empty;

    /// <summary>
    /// A deck's cards, or the components whose <see cref="ContainerRef"/> is this one, like a bag's or a tray's.
    /// </summary>
    /// <remarks>
    /// Any component used as a <see cref="ContainerRef"/> gets its contents here without changes.
    /// </remarks>
    public override IEnumerable<Target> Contents(IRecordReader R)
    {
        var containedComponents =
            R.Kind(this) == VisualComponentBase.VisualComponentType.Deck
                ? R.TokensOn(this)
                : R.Get<ComponentState>(c => c.ContainerRef == Id);

        return containedComponents.Select(c => new RecordTarget(c.Id));
    }

    /// <summary>
    /// The deck it's on or the bag it's in.
    /// </summary>
    /// <remarks>
    /// Any component referenced by <see cref="ContainerRef"/> becomes a container here without changes.
    /// </remarks>
    public override IEnumerable<Target> Containers(IRecordReader R)
    {
        if (IsContained)
            return R.Get<ComponentState>(ContainerRef) is { } container
                ? [new RecordTarget(container.Id)]
                : [];

        if (
            Location != VisualComponentBase.ComponentLocation.Table
            || R.Kind(this) != VisualComponentBase.VisualComponentType.Token
        )
            return [];

        // A filter may only read the record, so the kind is checked after.
        return R.Get<ComponentState>(d =>
                d.Location == VisualComponentBase.ComponentLocation.Table
                && d.X == X
                && d.Z == Z
                && d.ZOrder < ZOrder
            )
            .Where(d => R.Kind(d) == VisualComponentBase.VisualComponentType.Deck)
            .Select(d => new RecordTarget(d.Id));
    }

    /// <summary>
    /// Its prototype and, for a card made from a dataset, its row.
    /// </summary>
    /// <remarks>
    /// This will need to be extended as new references are invented.
    /// </remarks>
    public override IEnumerable<Target> Referenced(IRecordReader R) =>
        DataSetRowId == SnowTag.Empty
            ? [new RecordTarget(PrototypeRef)]
            : [new RecordTarget(PrototypeRef), new RecordTarget(DataSetRowId)];
}
