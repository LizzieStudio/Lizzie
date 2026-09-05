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
    protected readonly List<SnowportId> Children = new();

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

        var transformed = compArr
            .Select(
                (c, i) =>
                {
                    var t = TransformEffect.Capture(c);
                    t.Location = ComponentLocation.Container;
                    t.ContainerRef = Reference;
                    t.ZTarget = target;
                    t.ZSuborder = i;
                    return (Effect)t;
                }
            )
            .ToArray();

        return TableEvent.Now(new MoveAction(), transformed);
    }

    public override TableEvent DropObjects(IEnumerable<VisualComponentBase> dragObjects) =>
        AddChildComponents(dragObjects);

    protected abstract void OnChildrenChanged();

    public override IEnumerable<DeleteEffect> GetDespawnEffects()
    {
        foreach (var effect in base.GetDespawnEffects())
            yield return effect;

        foreach (var child in Children)
            yield return new DeleteEffect { ComponentRef = child };
    }

    /// <summary>
    /// Returns the top <paramref name="quantity"/> child ids, but does not remove them.
    /// </summary>
    public virtual SnowportId[] DrawFromTop(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        return quantity <= 0 ? Array.Empty<SnowportId>() : Children.Take(quantity).ToArray();
    }

    /// <summary>
    /// Returns the bottom <paramref name="quantity"/> child ids, but does not remove them.
    /// </summary>
    public virtual SnowportId[] DrawFromBottom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        return quantity <= 0
            ? Array.Empty<SnowportId>()
            : Children.TakeLast(quantity).Reverse().ToArray();
    }

    /// <summary>
    /// Picks <paramref name="quantity"/> random child ids, but does not remove them.
    /// </summary>
    public virtual IEnumerable<SnowportId> DrawRandom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);

        var pool = Children.ToList();
        var result = new List<SnowportId>(quantity);
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
    protected Effect[] BuildReorder(IReadOnlyList<SnowportId> orderedIds)
    {
        var effects = new List<Effect>(orderedIds.Count);
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(orderedIds[i]);
            if (comp == null)
                continue;

            var t = TransformEffect.Capture(comp);
            t.ZTarget = ZTarget.Top;
            t.ZSuborder = orderedIds.Count - 1 - i;
            effects.Add(t);
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
