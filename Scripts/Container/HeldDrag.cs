using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <para>The Godot drag-and-drop data of a gesture that holds components: what it holds, and its undo group.</para>
/// <para>
/// Where the components are lives in their atoms, which every player sees.
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
    public IReadOnlyList<StageKey> Components { get; init; } = [];

    /// <summary>
    /// The atoms of the components it holds.
    /// </summary>
    public IEnumerable<ComponentState> Atoms(IAtomReader R) =>
        Components.Select(key => R.Get(ComponentState.ById, key)).Where(s => s != null);

    /// <summary>
    /// The atoms of the held VcTokens, which can go into a hand.
    /// </summary>
    public IEnumerable<ComponentState> Cards(IAtomReader R) =>
        Atoms(R).Where(s => R.Kind(s) == VisualComponentBase.VisualComponentType.Token);
}
