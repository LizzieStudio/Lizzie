using System.Collections.Generic;
using System.Linq;
using Godot;

public abstract partial class VisualComponentGroup : VisualComponentBase
{
    public CollisionShape3D DragDropCollider { get; set; }

    /// <summary>
    /// The records that move <paramref name="dropped"/> into this container.
    /// </summary>
    public IEnumerable<Replicated> DropObjects(IEnumerable<ComponentState> dropped)
    {
        var stamp = Snowport.Clock.Create();
        return dropped.Select(
            Replicated (s, i) =>
                s with
                {
                    ContainerRef = Reference,
                    ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                }
        );
    }

    /// <summary>
    /// The records a drag on the container picks up, with positions as their offsets from the pointer,
    /// or none when there's nothing to draw. Nothing is written until the drag starts.
    /// </summary>
    public virtual ComponentState[] DragDraw() => [];
}
