using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public abstract partial class VisualGroupComponent : VisualComponentBase
{
    protected readonly List<VisualComponentBase> Children = new();

    protected RandomNumberGenerator Rnd = new();

    protected void Clear()
    {
        foreach (var c in Children)
        {
            c.QueueFree();
        }
    }
    
    public virtual void AddChildComponent(VisualComponentBase component)
    {
        component.Visible = false;
       Children.Add(component);
    }   

    public virtual void AddChildComponents(IEnumerable<VisualComponentBase> components)
    {
        var compArr = components as VisualComponentBase[] ?? components.ToArray(); //avoid multiple iterations
        foreach (var c in compArr) c.Visible = false;
        
        Children.AddRange(compArr);
    }

    /// <summary>
    /// Returns the first item in the group, and removes it.
    /// </summary>
    /// <param name="quantity"></param>
    /// <returns></returns>
    public virtual VisualComponentBase[] DrawFromTop(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0) return Array.Empty<VisualComponentBase>();

        var res = Children.Take(quantity).ToArray();
        
        Children.RemoveRange(0, quantity);
        
        return res;
    }

    /// <summary>
    /// Returns the last item in the group, and removes it
    /// </summary>
    /// <param name="quantity"></param>
    /// <returns></returns>
    public virtual VisualComponentBase[] DrawFromBottom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0) return Array.Empty<VisualComponentBase>();

        var res = Children.TakeLast(quantity).ToArray();
        
        
        Children.RemoveRange(Children.Count - quantity, quantity);

        return res;
    }

    /// <summary>
    /// Draws a single random item from the group, and removes it.
    /// </summary>
    /// <returns>A random item, which is removed from the group</returns>
    public virtual VisualComponentBase DrawRandom()
    {
        var r = Rnd.RandiRange(0, Children.Count - 1);
        var c = Children[r];

        c.Visible = true;
        
        Children.RemoveAt(r);
        return c;
    }

    /// <summary>
    /// Draws a random number of items from the group
    /// </summary>
    /// <param name="quantity">Qty to pull. If greater than the numberr of items
    /// in the group, pulls all of them (in a random order)</param>
    /// <returns>Components in a random order</returns>
    public virtual IEnumerable<VisualComponentBase> DrawRandom(int quantity)
    {
        quantity = Math.Min(quantity, Children.Count);
        if (quantity == 0) yield return null;

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
    }

    /// <summary>
    /// Reverses the order of the items in the group.
    /// Primary use is for when a deck or stack flips over
    /// </summary>
    public virtual void Reverse()
    {
        Children.Reverse();
    }
}
