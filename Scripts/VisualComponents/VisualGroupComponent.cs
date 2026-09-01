using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public abstract partial class VisualComponentGroup : VisualComponentBase
{
    protected readonly List<SnowportId> Children = new();

    protected RandomNumberGenerator Rnd = new();

    public CollisionShape3D DragDropCollider { get; set; }

    /// <summary>
    /// Deletes all contained visual objects
    /// </summary>
    protected void Clear()
    {
        foreach (var c in Children)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(c);
            comp?.QueueFree();
        }
        Children.Clear();
        OnChildrenChanged();
    }

    public override void Delete()
    {
        Clear();
        base.Delete();
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
        SyncRequired = true;

        var transformed = compArr
            .Select(c =>
            {
                var t = TransformedComponent.Capture(c);
                t.Location = ComponentLocation.Container;
                return t;
            })
            .ToArray();
        if (transformed.Length > 0)
        {
            EventSynchronizer.Instance?.Submit(
                new ComponentsTransformedEvent
                {
                    Id = Snowport.Clock.Create(),
                    Components = transformed,
                }
            );
        }
    }

    public override void DropObjects(IEnumerable<VisualComponentBase> dragObjects)
    {
        AddChildComponents(dragObjects);
    }

    protected abstract void OnChildrenChanged();

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
        SyncRequired = true;

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
        SyncRequired = true;

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
        SyncRequired = true;

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
    }

    /// <summary>
    /// Reverses the order of the items in the group.
    /// Primary use is for when a deck or stack flips over
    /// </summary>
    public virtual void Reverse()
    {
        Children.Reverse();
        OnChildrenChanged();
        SyncRequired = true;
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
