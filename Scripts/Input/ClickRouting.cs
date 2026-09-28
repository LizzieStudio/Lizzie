using System.Linq;
using Godot;

/// <summary>
/// Makes clicks for right-click context menus behave as expected.
/// 
/// This is basically just a series of fixes for Godot weirdnesses.
/// Most people should never have to interact with class.
/// </summary>
public static class ClickRouting
{
    private static bool _installed;

    // Where the right button last went down, and whether it came back up without a drag.
    private static Vector2 _rightPressAt;
    private static bool _rightClicked;

    /// <summary>Whether <paramref name="e"/> is the release of a right-click, which opens a context menu.</summary>
    public static bool OpensMenu(InputEvent e) =>
        e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }
        && _rightClicked;

    /// <summary>Applies the rules to every click in <paramref name="root"/>, the main window.</summary>
    public static void Install(Window root)
    {
        if (_installed)
            return;
        _installed = true;
        // Emitted for each event the system gives the window, before it goes to an embedded window.
        root.WindowInput += e => OnWindowInput(root, e);
    }

    private static void OnWindowInput(Window root, InputEvent e)
    {
        // The wheel is a button too, but scrolling shouldn't move focus or close the menu.
        if (
            e
            is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle,
            } button
        )
            return;

        if (button.ButtonIndex == MouseButton.Right)
        {
            if (button.Pressed)
                _rightPressAt = button.Position;
            else
                // Moving less than Godot's drag threshold is a click.
                _rightClicked = button.Position.DistanceTo(_rightPressAt) < root.GuiDragThreshold;
        }
        if (!button.Pressed)
            return;

        // First, since closing the menu hands focus back to the window under it.
        CommandMenu.CloseUnlessOver(button.Position);

        if (button.ButtonIndex != MouseButton.Left)
            FocusWindowAt(root, button.Position);
    }

    /// <summary>Focuses the embedded window at <paramref name="at"/>, or the main window if there's none.</summary>
    private static void FocusWindowAt(Window root, Vector2 at)
    {
        // Bottom to top.
        var windows = root.GetEmbeddedSubwindows().Where(w => w.Visible).ToList();
        var under = windows.LastOrDefault(w => Frame(w).HasPoint(at));
        if (under != null)
        {
            if (!under.HasFocus())
                under.GrabFocus();
            return;
        }

        if (!windows.Any(w => w.HasFocus()))
            return;

        // Godot has no call to give focus back to the main window. Focusing a window that can't take focus
        // takes it from the focused one instead, and brings that window to the front,
        // so it's done to the window already there.
        var top = windows[^1];
        top.Unfocusable = true;
        top.GrabFocus();
        top.Unfocusable = false;
    }

    // The window with its title bar, which Godot counts as part of it.
    private static Rect2 Frame(Window w)
    {
        var frame = new Rect2(w.Position, w.Size);
        return w.Borderless ? frame : frame.GrowSide(Side.Top, w.GetThemeConstant("title_height"));
    }
}
