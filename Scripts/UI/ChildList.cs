using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Converts an ordered List into ordered child nodes, and keeps them up-to-date.
/// </summary>
/// <param name="parent">The node whose children these are.</param>
/// <param name="create">Makes the child for a key and adds it to <paramref name="parent"/>.</param>
/// <typeparam name="K">What tells the items apart, like their ids.</typeparam>
/// <typeparam name="N">The type of the children.</typeparam>
public sealed class ChildList<K, N>(Node parent, Func<K, N> create)
    where N : Node
{
    private readonly Dictionary<K, N> _children = new();

    /// <summary>
    /// The child for <paramref name="key"/>.
    /// </summary>
    public N this[K key] => _children[key];

    /// <summary>
    /// Inside of a <c>Watch(this, Sync)</c> call, pass the <paramref name="items"/> here.
    ///
    /// <para>
    /// This method will update the child nodes in three ways:
    /// <list type="bullet">
    /// <item>New items are created</item>
    /// <item>Existing items are reordered</item>
    /// <item>Missing items are removed</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="items">The list to turn into the nodes.</param>
    /// <param name="keyOf">A function to compute the item keys.</param>
    /// <returns>The children, in order.</returns>
    public IReadOnlyList<N> Show<T>(IReadOnlyList<T> items, Func<T, K> keyOf)
    {
        var keys = items.Select(keyOf).ToList();
        foreach (var gone in _children.Keys.Except(keys).ToList())
        {
            _children.Remove(gone, out var child);
            parent.RemoveChild(child);
            child.QueueFree();
        }

        var shown = new N[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            if (!_children.TryGetValue(keys[i], out var child))
                _children[keys[i]] = child = create(keys[i]);
            parent.MoveChild(child, i);
            shown[i] = child;
        }
        return shown;
    }
}
