using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// The <see cref="InputMap"/> actions that commands' keys and the number keys are registered as,
/// and their labels for menus.
/// </summary>
public static class ShortcutActions
{
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
        yield return Shortcuts.Key(TopRow[i], shift);
        yield return Shortcuts.Key(Keypad[i], shift);
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
