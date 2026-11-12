using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Utilities for <see cref="ICommandView"/>.
/// </summary>
public static class CommandViews
{
    private static readonly Dictionary<Node, ICommandView> Attached = new();

    /// <summary>
    /// Makes <paramref name="view"/> take the <see cref="Command"/>s
    /// for everything under <paramref name="node"/> until either leaves the tree.
    /// </summary>
    public static void Attach(Node node, ICommandView view)
    {
        Attached[node] = view;
        DetachOnExit(node, node, view);
        if (view is Node owner && owner != node)
            DetachOnExit(owner, node, view);
    }

    private static void DetachOnExit(Node leaving, Node node, ICommandView view) =>
        leaving.Connect(
            Node.SignalName.TreeExiting,
            Callable.From(() =>
            {
                // Don't remove it if it was Attached to a new view.
                if (Attached.GetValueOrDefault(node) == view)
                    Attached.Remove(node);
            }),
            (uint)GodotObject.ConnectFlags.OneShot // our callback is only called once
        );

    /// <summary>
    /// The view for the viewport's focused control, or null if it has none.
    /// </summary>
    public static ICommandView Find(Viewport viewport) =>
        Of(viewport.GuiGetFocusOwner() ?? (Node)viewport);

    /// <summary>
    /// The focused embedded window, or <paramref name="root"/>.
    /// </summary>
    public static Window FocusedWindow(Window root) =>
        root.GetEmbeddedSubwindows()
            .FirstOrDefault(w => w.Visible && w.HasFocus() && w is not Popup)
        ?? root;

    /// <summary>
    /// The view that <paramref name="node"/> is in.
    /// </summary>
    public static ICommandView Of(Node node)
    {
        for (Node n = node; n != null; n = n.GetParent())
        {
            if (n is ICommandView view)
                return view;
            if (Attached.TryGetValue(n, out var attached))
                return attached;
            // A window only takes its own views, not the ones of the windows it's inside.
            if (n is Window)
                break;
        }
        return null;
    }

    /// <summary>
    /// The view that <paramref name="node"/> is in, for a write from it, since undo finds writes in their view.
    /// Throws, saying why, when it's in none.
    /// </summary>
    public static ICommandView Require(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!GodotObject.IsInstanceValid(node))
            throw new InvalidOperationException(
                $"A {node.GetType().Name} wrote after it was freed. Write before freeing it."
            );
        if (Of(node) is { } view)
            return view;
        if (!node.IsInsideTree())
            throw new InvalidOperationException(
                $"{node.Name} wrote while out of the tree, with no view above it. Add it under its view first, or write from the view."
            );
        var window = node.GetWindow();
        if (window.GetParent() is not { } parent)
            throw new InvalidOperationException(
                $"{node.Name} wrote in the main window while no view was attached to it. Write once the table has loaded."
            );
        var where =
            node == window
                ? $"{window.Name} ({window.GetType().Name}) wrote, but isn't a view."
                : $"{node.Name} wrote from {window.Name} ({window.GetType().Name}), which isn't a view.";
        throw Of(parent) is { } outer
            ? new InvalidOperationException(
                $"{where} Write from {outer.GetType().Name}, the view around it, or make {window.Name} a view."
            )
            : new InvalidOperationException($"{where} Make {window.Name} a view.");
    }
}
