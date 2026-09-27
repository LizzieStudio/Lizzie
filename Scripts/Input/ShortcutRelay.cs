using System;
using Godot;

/// <summary>
/// Keyboard shortcut helpers.
/// This helps when adding shortcuts to dialogues,
/// who get their own viewport and therefore capture their own shortcuts.
/// </summary>
public static class Shortcuts
{
    /// <summary>
    /// True when the event presses the action with exactly its modifiers.
    /// Extra held modifiers never trigger a smaller binding: Shift+1 isn't 1.
    /// </summary>
    public static bool Pressed(InputEvent e, StringName action) =>
        e.IsActionPressed(action, allowEcho: false, exactMatch: true);

    /// <summary>
    /// Runs the commands bound to the key, in the view that has the viewport's focus.
    /// True when the key belongs to a command.
    /// </summary>
    public static bool Dispatch(InputEvent e, Viewport viewport) =>
        e is InputEventKey
        && CommandList.RunShortcut(e, CommandViews.Find(viewport)?.BuildContext());
}

/// <summary>
/// Passes shortcuts to <see cref="Shortcuts.Dispatch"/>.
/// Windows don't pass input to the table, so this needs to be added to every dialogue.
/// It has an <see cref="Install"/> to do so.
/// </summary>
public partial class ShortcutRelay : Node
{
    private static bool _installed;

    /// <summary>
    /// Installs the ShortcutRelay.
    /// The ShotcutRelay attaches itself to all current and future Window nodes by subscribing to tree changes.
    /// </summary>
    /// <param name="tree">Pass GetTree()</param>
    public static void Install(SceneTree tree)
    {
        if (_installed)
            return;
        _installed = true;
        AddTo(tree.Root);
        foreach (var node in tree.Root.FindChildren("*", nameof(Window), true, false))
            AddTo((Window)node);
        tree.NodeAdded += node =>
        {
            if (node is Window window)
                AddTo(window);
        };
    }

    // Deferred because NodeAdded fires too early to add children.
    private static void AddTo(Window window) =>
        Callable
            .From(() =>
            {
                if (IsInstanceValid(window))
                    window.AddChild(new ShortcutRelay());
            })
            .CallDeferred();

    public override void _ShortcutInput(InputEvent e)
    {
        if (Shortcuts.Dispatch(e, GetViewport()))
            GetViewport().SetInputAsHandled();
    }

    private static readonly MouseButton[] _defocusEvents =
    [
        MouseButton.Left,
        MouseButton.Right,
        MouseButton.Middle,
    ];

    // If a click happens outside of the currently focused control, defocus it.
    // Runs before the GUI, so a control that takes focus on click still gets it.
    public override void _Input(InputEvent e)
    {
        if (
            e is InputEventMouseButton click
            && click.Pressed
            && _defocusEvents.Contains(click.ButtonIndex)
        )
        {
            var focus = GetViewport().GuiGetFocusOwner();
            if (focus == null)
                return;

            // local is the position of the click relative to the corner of the focused input
            var local = focus.GetGlobalTransformWithCanvas().AffineInverse() * click.Position;
            // if the position of the click is not in the focused input, forcably defocus the input
            if (!new Rect2(Vector2.Zero, focus.Size).HasPoint(local))
                GetViewport().GuiReleaseFocus();
        }
    }
}
