using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <para>The Godot drag-and-drop data of a gesture that holds components: what it holds, and its undo group.</para>
/// <para>
/// Where the components are lives in their records, which every player sees.
/// A drop target reads them from there, and accepts the drag by closing <see cref="Group"/> with the drop.
/// A drag that ends with its group still open wasn't accepted, and is canceled.
/// </para>
/// </summary>
public sealed partial class HeldDrag : RefCounted
{
    /// <summary>
    /// The gesture's undo group, opened by the pickup.
    /// </summary>
    public SnowportId Group { get; init; }

    /// <summary>
    /// Every component the gesture picked up.
    /// </summary>
    public IReadOnlyList<SnowTag> Components { get; init; } = [];

    /// <summary>
    /// The records of the components it holds.
    /// </summary>
    public IEnumerable<ComponentState> Records(IRecordReader R) =>
        R.Get<ComponentState>(Components);

    /// <summary>
    /// The records of the held VcTokens, which can go into a hand.
    /// </summary>
    public IEnumerable<ComponentState> Cards(IRecordReader R) =>
        Records(R).Where(s => R.Kind(s) == VisualComponentBase.VisualComponentType.Token);
}
