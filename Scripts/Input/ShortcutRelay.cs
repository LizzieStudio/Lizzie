using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

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

    /// <summary>The action's first key, to show beside a menu item, or null.</summary>
    public static Shortcut Label(StringName action)
    {
        if (action == null || !InputMap.HasAction(action))
            return null;
        var key = InputMap.ActionGetEvents(action).OfType<InputEventKey>().FirstOrDefault();
        if (key == null)
            return null;

        // Bindings by physical key have no label of their own, so show the key it types.
        if (key.Keycode == Godot.Key.None && key.PhysicalKeycode != Godot.Key.None)
        {
            key = (InputEventKey)key.Duplicate();
            key.Keycode = DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode);
            key.PhysicalKeycode = Godot.Key.None;
        }
        return new Shortcut { Events = [key] };
    }

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

    /// <summary>How many numbers the number keys give.</summary>
    public const int NumberCount = 20;

    /// <summary>The action for number key <paramref name="n"/>, from 1 to <see cref="NumberCount"/>.</summary>
    public static StringName Number(int n) => $"num_{n}";

    // 1–9 and 0 on the top row and the keypad give 1–10, and with Shift, 11–20.
    private static readonly Key[] TopRow =
    [
        Godot.Key.Key1,
        Godot.Key.Key2,
        Godot.Key.Key3,
        Godot.Key.Key4,
        Godot.Key.Key5,
        Godot.Key.Key6,
        Godot.Key.Key7,
        Godot.Key.Key8,
        Godot.Key.Key9,
        Godot.Key.Key0,
    ];

    private static readonly Key[] Keypad =
    [
        Godot.Key.Kp1,
        Godot.Key.Kp2,
        Godot.Key.Kp3,
        Godot.Key.Kp4,
        Godot.Key.Kp5,
        Godot.Key.Kp6,
        Godot.Key.Kp7,
        Godot.Key.Kp8,
        Godot.Key.Kp9,
        Godot.Key.Kp0,
    ];

    /// <summary>The keys that give number <paramref name="n"/>.</summary>
    public static IEnumerable<InputEventKey> NumberKeys(int n)
    {
        bool shift = n > 10;
        int i = (n - 1) % 10;
        yield return Key(TopRow[i], shift);
        yield return Key(Keypad[i], shift);
    }

    /// <summary>Adds an action to the <see cref="InputMap"/> with <paramref name="keys"/>, replacing any it had.</summary>
    public static void AddAction(StringName action, IEnumerable<InputEventKey> keys)
    {
        if (InputMap.HasAction(action))
            InputMap.EraseAction(action);
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, key);
    }
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
