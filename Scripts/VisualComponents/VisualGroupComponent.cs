using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public abstract partial class VisualComponentGroup : VisualComponentBase
{
    /// <summary>
    /// A cache of this container's contents, top first.
    /// The source-of-truth is <see cref="ComponentState.ContainerRef"/>.
    /// </summary>
    protected readonly List<SnowTag> Children = new();

    protected RandomNumberGenerator Rnd = new();

    public CollisionShape3D DragDropCollider { get; set; }

    /// <summary>
    /// Recomputes the child cache from the records this container holds.
    /// </summary>
    public void RebuildCache()
    {
        var ordered = RecordService
            .Instance.Get<ComponentState>(s => s.ContainerRef == Reference)
            .OrderByDescending(s => s.ZOrder)
            .Select(s => s.Id)
            .ToList();

        if (ordered.Count == Children.Count && ordered.SequenceEqual(Children))
            return;

        Children.Clear();
        Children.AddRange(ordered);
        OnChildrenChanged();
    }

    /// <summary>
    /// The records that move the given components into this container.
    /// </summary>
    public override IEnumerable<Replicated> DropObjects(
        IEnumerable<VisualComponentBase> dragObjects
    )
    {
        var compArr = dragObjects as VisualComponentBase[] ?? dragObjects.ToArray(); //avoid multiple iterations
        if (compArr.Length == 0)
            return [];

        var target = ZTarget.Top;
        var stamp = Snowport.Clock.Create();

        return compArr.Select(
            Replicated (c, i) =>
                ComponentState.Of(c) with
                {
                    Location = ComponentLocation.Container,
                    ContainerRef = Reference,
                    Position = c.Position,
                    ZOrder = new ZOrder(target, i, stamp),
                }
        );
    }

    protected abstract void OnChildrenChanged();

    /// <summary>
    /// Returns the top <paramref name="quantity"/> child ids, but does not remove them.
    /// </summary>
    public virtual SnowTag[] DrawFromTop(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        return quantity <= 0 ? Array.Empty<SnowTag>() : Children.Take(quantity).ToArray();
    }

    /// <summary>
    /// Returns the bottom <paramref name="quantity"/> child ids, but does not remove them.
    /// </summary>
    public virtual SnowTag[] DrawFromBottom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        return quantity <= 0
            ? Array.Empty<SnowTag>()
            : Children.TakeLast(quantity).Reverse().ToArray();
    }

    /// <summary>
    /// Picks <paramref name="quantity"/> random child ids, but does not remove them.
    /// </summary>
    public virtual IEnumerable<SnowTag> DrawRandom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);

        var pool = Children.ToList();
        var result = new List<SnowTag>(quantity);
        for (int i = 0; i < quantity; i++)
        {
            int r = Rnd.RandiRange(0, pool.Count - 1);
            result.Add(pool[r]);
            pool.RemoveAt(r);
        }

        return result;
    }

    /// <summary>
    /// Shuffles the container using a seed and the Fisher-Yates algorithm.
    /// </summary>
    public virtual Replicated[] Shuffle(ulong seed)
    {
        var ids = Children.ToList();
        ids.Sort(); // deterministic starting order

        var rng = new RandomNumberGenerator { Seed = seed };
        int n = ids.Count - 1;
        while (n > 0)
        {
            var r = rng.RandiRange(0, n);
            (ids[r], ids[n]) = (ids[n], ids[r]);
            n--;
        }

        return BuildReorder(ids);
    }

    /// <summary>
    /// The records that reorder the child list
    /// </summary>
    protected Replicated[] BuildReorder(IReadOnlyList<SnowTag> orderedIds)
    {
        var records = new List<Replicated>(orderedIds.Count);
        var stamp = Snowport.Clock.Create();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(orderedIds[i]);
            if (comp == null)
                continue;

            records.Add(
                ComponentState.Of(comp) with
                {
                    ZOrder = new ZOrder(ZTarget.Top, orderedIds.Count - 1 - i, stamp),
                }
            );
        }

        return records.ToArray();
    }

    /// <summary>
    /// Called when the user drags on a container to draw components, or uses a key command to
    /// draw multiples. The records the draw writes, or none if it draws nothing.
    /// </summary>
    /// <param name="quantity"></param>
    public virtual Replicated[] DragDraw(int quantity) => [];
}
