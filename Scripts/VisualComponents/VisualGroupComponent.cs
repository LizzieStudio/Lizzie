using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public abstract partial class VisualComponentGroup : VisualComponentBase
{
    /// <summary>
    /// A cache of this container's contents.
    /// The source-of-truth is <see cref="VisualComponentBase.ContainerRef"/>.
    /// </summary>
    protected readonly List<SnowTag> Children = new();

    protected RandomNumberGenerator Rnd = new();

    public CollisionShape3D DragDropCollider { get; set; }

    /// <summary>
    /// Recomputes the child cache from the <see cref="VisualComponentBase.ContainerRef"/> of children.
    /// </summary>
    public void RebuildCache(IEnumerable<VisualComponentBase> all)
    {
        var ordered = all.Where(c => c.ContainerRef == Reference)
            .OrderByDescending(c => c.ZOrder)
            .Select(c => c.Reference)
            .ToList();

        if (ordered.Count == Children.Count && ordered.SequenceEqual(Children))
            return;

        Children.Clear();
        Children.AddRange(ordered);
        OnChildrenChanged();
    }

    /// <summary>
    /// Builds the event that moves the given components into this container,
    /// or null if there are none.
    /// </summary>
    public virtual TableEvent AddChildComponents(
        IEnumerable<VisualComponentBase> components,
        bool addToTop = false
    )
    {
        var compArr = components as VisualComponentBase[] ?? components.ToArray(); //avoid multiple iterations
        if (compArr.Length == 0)
            return null;

        var target = addToTop ? ZTarget.Top : ZTarget.Bottom;
        var stamp = Snowport.Clock.Create();

        var transformed = compArr
            .Select(
                (c, i) =>
                {
                    var e = ComponentEffect.Capture(c);
                    e.State.Location = ComponentLocation.Container;
                    e.State.ContainerRef = Reference;
                    e.State.Position = c.Position;
                    e.State.ZOrder = new ZOrder(target, i, stamp);
                    return (Effect)e;
                }
            )
            .ToArray();

        return TableEvent.Now(new MoveAction(), transformed);
    }

    public override TableEvent DropObjects(IEnumerable<VisualComponentBase> dragObjects) =>
        AddChildComponents(dragObjects);

    protected abstract void OnChildrenChanged();

    public override IEnumerable<ComponentEffect> GetDespawnEffects()
    {
        foreach (var effect in base.GetDespawnEffects())
            yield return effect;

        foreach (var child in Children)
            yield return new ComponentEffect
            {
                Id = child,
                State = new VcSyncDto { Location = ComponentLocation.Deleted },
            };
    }

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
    public virtual Effect[] Shuffle(ulong seed)
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
    /// Builds the transform effects that reorder the child list
    /// </summary>
    protected Effect[] BuildReorder(IReadOnlyList<SnowTag> orderedIds)
    {
        var effects = new List<Effect>(orderedIds.Count);
        var stamp = Snowport.Clock.Create();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(orderedIds[i]);
            if (comp == null)
                continue;

            var e = ComponentEffect.Capture(comp);
            e.State.ZOrder = new ZOrder(ZTarget.Top, orderedIds.Count - 1 - i, stamp);
            effects.Add(e);
        }

        return effects.ToArray();
    }

    /// <summary>
    /// Called when the user drags on a container to draw components, or uses a key command to
    /// draw multiples. Builds the event the draw should fire, or null if it fires none.
    /// </summary>
    /// <param name="quantity"></param>
    public virtual TableEvent DragDraw(int quantity) => null;
}
