using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public abstract partial class VisualComponentGroup : VisualComponentBase
{
    public CollisionShape3D DragDropCollider { get; set; }

    /// <summary>
    /// The atoms that move <paramref name="dropped"/> into this container.
    /// </summary>
    public IEnumerable<Atom> DropObjects(IReadOnlyList<ComponentPosition> dropped)
    {
        var ranks = SnowRank.AfterAll(dropped.Count);
        return dropped.Select(
            Atom (s, i) => s with { Container = new InComponent(ComponentRef), Rank = ranks[i] }
        );
    }

    /// <summary>
    /// The atoms a drag on the container picks up, with positions as their offsets from the pointer,
    /// or none when there's nothing to draw. Nothing is written until the drag starts.
    /// Any additional atoms that should be written for a drag are included in the second value.
    /// </summary>
    public virtual (ComponentPosition[], Atom[]) DragDraw() => ([], []);
}
