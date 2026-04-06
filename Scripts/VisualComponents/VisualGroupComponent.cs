using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public abstract partial class VisualComponentGroup : VisualComponentBase
{
    protected readonly List<Guid> Children = new();

    protected RandomNumberGenerator Rnd = new();

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

    public virtual void AddChildComponent(VisualComponentBase component)
    {
        component.Location = ComponentLocation.Container;
        Children.Add(component.Reference);
        OnChildrenChanged();
    }

    public virtual void AddChildComponents(IEnumerable<VisualComponentBase> components)
    {
        var compArr = components as VisualComponentBase[] ?? components.ToArray(); //avoid multiple iterations
        foreach (var c in compArr)
        {
            c.Location = ComponentLocation.Container;
            Children.Add(c.Reference);
        }

        OnChildrenChanged();
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
    public virtual Guid[] DrawFromTop(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0)
            return Array.Empty<Guid>();

        var res = Children.Take(quantity).ToArray();

        Children.RemoveRange(0, quantity);
        OnChildrenChanged();

        return res;
    }

    /// <summary>
    /// Returns the last item in the group, and removes it
    /// </summary>
    /// <param name="quantity"></param>
    /// <returns></returns>
    public virtual Guid[] DrawFromBottom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0)
            return Array.Empty<Guid>();

        var res = Children.TakeLast(quantity).ToArray();

        Children.RemoveRange(Children.Count - quantity, quantity);
        OnChildrenChanged();

        return res;
    }

    /// <summary>
    /// Draws a single random item from the group, and removes it.
    /// </summary>
    /// <returns>A random item, which is removed from the group</returns>
    public virtual Guid DrawRandom()
    {
        var r = Rnd.RandiRange(0, Children.Count - 1);
        var c = Children[r];

        Children.RemoveAt(r);
        OnChildrenChanged();

        return c;
    }

    /// <summary>
    /// Draws a random number of items from the group
    /// </summary>
    /// <param name="quantity">Qty to pull. If greater than the numberr of items
    /// in the group, pulls all of them (in a random order)</param>
    /// <returns>Components in a random order</returns>
    public virtual IEnumerable<Guid> DrawRandom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0)
            yield return Guid.Empty;

        for (int i = 0; i < quantity; i++)
        {
            yield return DrawRandom();
        }
    }

    /// <summary>
    /// Shuffles the group using the Fisher-Yates algorithm
    /// </summary>
    public virtual void Shuffle()
    {
        int n = Children.Count - 1;

        while (n > 0)
        {
            var r = Rnd.RandiRange(0, n);

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
    }

    public Guid[] GetContainerChildren()
    {
        return Children.ToArray();
    }

    public void SetContainerChildren(Guid[] children)
    {
        Children.Clear();
        Children.AddRange(children);
        OnChildrenChanged();
    }
}
