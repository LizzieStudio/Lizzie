using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public abstract partial class VisualComponentGroup : VisualComponentBase
{
    protected readonly List<SnowportId> Children = new();

    protected RandomNumberGenerator Rnd = new();

    /// <summary>The event that most recently restructured this container's children.</summary>
    public SnowportId LastRestructureId { get; set; } = SnowportId.Empty;

    public CollisionShape3D DragDropCollider { get; set; }

    /// <summary>
    /// Broadcasts this container's current child list as a RestructureEffect.
    /// </summary>
    protected void EmitRestructure()
    {
        EventSynchronizer.Instance?.Submit(TableEvent.Now(null, RestructureEffect.Capture(this)));
    }

    public virtual void AddChildComponents(
        IEnumerable<VisualComponentBase> components,
        bool addToTop = false
    )
    {
        var compArr = components as VisualComponentBase[] ?? components.ToArray(); //avoid multiple iterations
        foreach (var c in compArr)
        {
            if (addToTop)
                Children.Insert(0, c.Reference);
            else
                Children.Add(c.Reference);
        }

        OnChildrenChanged();

        var transformed = compArr
            .Select(c =>
            {
                var t = TransformEffect.Capture(c);
                t.Location = ComponentLocation.Container;
                return (Effect)t;
            })
            .Append(RestructureEffect.Capture(this))
            .ToArray();
        if (transformed.Length > 0)
        {
            EventSynchronizer.Instance?.Submit(TableEvent.Now(new MoveAction(), transformed));
        }
    }

    public override void DropObjects(IEnumerable<VisualComponentBase> dragObjects)
    {
        AddChildComponents(dragObjects);
    }

    protected abstract void OnChildrenChanged();

    public override IEnumerable<DeleteEffect> GetDespawnEffects()
    {
        foreach (var effect in base.GetDespawnEffects())
            yield return effect;

        foreach (var child in Children)
            yield return new DeleteEffect { ComponentRef = child };
    }

    /// <summary>
    /// Returns the first item in the group, and removes it.
    /// </summary>
    /// <param name="quantity"></param>
    /// <returns></returns>
    public virtual SnowportId[] DrawFromTop(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0)
            return Array.Empty<SnowportId>();

        var res = Children.Take(quantity).ToArray();

        Children.RemoveRange(0, quantity);
        OnChildrenChanged();
        EmitRestructure();

        return res;
    }

    /// <summary>
    /// Returns the last item in the group, and removes it
    /// </summary>
    /// <param name="quantity"></param>
    /// <returns></returns>
    public virtual SnowportId[] DrawFromBottom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0)
            return Array.Empty<SnowportId>();

        var res = Children.TakeLast(quantity).ToArray();

        Children.RemoveRange(Children.Count - quantity, quantity);
        OnChildrenChanged();
        EmitRestructure();

        return res;
    }

    /// <summary>
    /// Draws a single random item from the group, and removes it.
    /// </summary>
    /// <returns>A random item, which is removed from the group</returns>
    protected virtual SnowportId DrawRandom()
    {
        var r = Rnd.RandiRange(0, Children.Count - 1);
        var c = Children[r];

        Children.RemoveAt(r);
        OnChildrenChanged();
        EmitRestructure();

        return c;
    }

    /// <summary>
    /// Draws a specific number of random items from the group, or fewer if items run out.
    /// </summary>
    /// <param name="quantity">Number to pull. If greater than the number of items
    /// in the group, pulls all of them (in a random order)</param>
    /// <returns>Components in a random order</returns>
    public virtual IEnumerable<SnowportId> DrawRandom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);

        for (int i = 0; i < quantity; i++)
        {
            yield return DrawRandom();
        }
    }

    /// <summary>
    /// Shuffles the group using a seed and the Fisher-Yates algorithm
    /// </summary>
    public virtual void Shuffle(ulong seed)
    {
        Children.Sort();

        var rng = new RandomNumberGenerator { Seed = seed };

        int n = Children.Count - 1;

        while (n > 0)
        {
            var r = rng.RandiRange(0, n);

            (Children[r], Children[n]) = (Children[n], Children[r]);
            n--;
        }

        OnChildrenChanged();
        EmitRestructure();
    }

    /// <summary>
    /// Reverses the order of the items in the group.
    /// Primary use is for when a deck or stack flips over
    /// </summary>
    public virtual void Reverse()
    {
        Children.Reverse();
        OnChildrenChanged();
        EmitRestructure();
    }

    public SnowportId[] GetContainerChildren()
    {
        return Children.ToArray();
    }

    public void SetContainerChildren(SnowportId[] children)
    {
        Children.Clear();
        Children.AddRange(children);
        OnChildrenChanged();
    }

    /// <summary>
    /// Called when the user drags on a container to draw components, or uses a key command to draw multiples
    /// </summary>
    /// <param name="quantity"></param>
    public virtual void DragDraw(int quantity) { }
}
