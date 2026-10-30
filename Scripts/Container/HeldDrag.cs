using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// The Godot drag-and-drop data of a gesture that holds components.
///
/// <para>
/// Where the components are lives in their atoms, which every player sees.
/// A drop target reads them from there, and accepts the drag by closing <see cref="Transition"/> with the drop,
/// which is all of the drag that's kept.
/// A drag that ends with its transition still open wasn't accepted, and is canceled.
/// </para>
/// </summary>
public sealed partial class HeldDrag : RefCounted
{
    /// <summary>
    /// The transition, opened by the pickup.
    /// </summary>
    public Transition Transition { get; init; }

    /// <summary>
    /// Every component the transition picked up.
    /// </summary>
    public IReadOnlyList<StageKey<ComponentPosition>> Components { get; init; } = [];

    /// <summary>
    /// The other atoms the pickup wrote, like a tray's new component.
    /// </summary>
    public IReadOnlyList<IAtomRef> Alongside { get; init; } = [];

    /// <summary>
    /// The atoms of the components it still holds.
    /// Another player can take one away, by unsetting it, say.
    /// </summary>
    public IEnumerable<ComponentPosition> Atoms(IAtomReader R) =>
        Components
            .Select(key => R.Get(ComponentPosition.ById, key))
            .Where(s => s?.Transition == Transition);

    /// <summary>
    /// The atoms the pickup wrote alongside the components, as they are now, for the drop to keep.
    /// </summary>
    public IEnumerable<Atom> KeptAlongside(IAtomReader R) =>
        Alongside.Select(R.Get).Where(a => a?.Transition == Transition);

    /// <summary>
    /// The atoms of the held VcPrinteds, which can go into a hand.
    /// </summary>
    public IEnumerable<ComponentPosition> Cards(IAtomReader R) =>
        Atoms(R).Where(s => R.Kind(s) == VisualComponentBase.VisualComponentType.Printed);
}
