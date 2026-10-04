using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

/// <summary>
/// Keys for a <see cref="Command"/>'s <see cref="Command.Keys"/>, and checking a key press.
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
    /// A key by its place on the keyboard, whatever it types on the player's layout.
    /// Game keys work this way, so they stay together on any layout.
    /// </summary>
    public static InputEventKey Key(Key key, bool shift = false) =>
        new() { PhysicalKeycode = key, ShiftPressed = shift };

    /// <summary>A key by what it types, held with Ctrl, or Cmd on macOS.</summary>
    public static InputEventKey Ctrl(Key key, bool shift = false, bool alt = false) =>
        // Autoremap picks Ctrl or Cmd, and setting Ctrl as well is an error.
        new()
        {
            Keycode = key,
            CommandOrControlAutoremap = true,
            ShiftPressed = shift,
            AltPressed = alt,
        };
}

/// <summary>
/// Runs the command bound to a key, in the view that has the window's focus.
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
        if (Dispatch(e, GetViewport()))
            GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Runs the command bound to the key, in the view that has the viewport's focus.
    /// True when the key belongs to a command.
    /// </summary>
    private static bool Dispatch(InputEvent e, Viewport viewport) =>
        e is InputEventKey && CommandResolver.RunShortcut(e, CommandViews.Find(viewport));

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
