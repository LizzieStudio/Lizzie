using System.Linq;
using Godot;

/// <summary>
/// <para>Makes clicks for right-click context menus behave as expected.</para>
///
/// <para>
/// A right-click anywhere opens the command menu for the view under it.
/// Views can select on the key-down event to add the target to the selection.
/// Other than that, an <see cref="ICommandView"/> doesn't have to do anything.
/// </para>
///
/// This is otherwise a series of fixes for Godot weirdnesses.
/// Most people should never have to interact with this class.
/// </summary>
public static class ClickRouting
{
    private static bool _installed;

    // Where the right button last went down. Used for a threshold distance.
    private static Vector2 _rightPressAt;

    // Whether the held right button has moved past the threshold, making it a drag, not a click.
    // This is a "threshold switch". Once it's past that point, moving back to the start doesn't reverse it.
    private static bool _rightDragged;

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
        if (
            e is InputEventMouseMotion motion
            && motion.ButtonMask.HasFlag(MouseButtonMask.Right)
            && motion.Position.DistanceTo(_rightPressAt) >= root.GuiDragThreshold
        )
            _rightDragged = true;

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
            {
                _rightPressAt = button.Position;
                _rightDragged = false;
            }
            else if (!_rightDragged)
                OpenMenu(root, button.Position);
        }
        if (!button.Pressed)
            return;

        // First, since closing the menu hands focus back to the window under it.
        CommandMenu.CloseUnlessOver(button.Position);

        if (button.ButtonIndex != MouseButton.Left)
            FocusWindowAt(root, button.Position);
    }

    private static void OpenMenu(Window root, Vector2 at)
    {
        var window = WindowAt(root, at) ?? root;

        if (
            window is Popup
            || root.GetEmbeddedSubwindows().Any(w => w.Visible && w.Exclusive && w != window)
            || ImGuiInterop.ClaimingMouse
        )
            return;

        var control = window.GuiGetHoveredControl();
        while (control != null && control.MouseFilter != Control.MouseFilterEnum.Stop)
            control = control.GetParentControl();
        // A text box keeps the Godot native context menu.
        // This is similar to the strategy of applications like Google Sheets.
        if (control is LineEdit or TextEdit && control.HasFocus())
            return;

        var view = CommandViews.Of(control ?? (Node)window);
        // In the main window, controls like the menu bar draw over the table but aren't part of it.
        // They reach the table's view (arriving here) but we don't want to open a menu for them.
        if (window == root && control != null && view == CommandViews.Of(root))
            return;
        // Don't open a menu mid-drag
        if (view == null || Input.IsMouseButtonPressed(MouseButton.Left))
            return;

        // We defer so that the view can handle the click first.
        // This is often used to select the item clicked so that it's included.
        Callable
            .From(() =>
            {
                // Writes a text box being edited, so the menu acts on what it shows.
                window.GuiReleaseFocus();
                CommandMenu.Show((Vector2I)at, view);
            })
            .CallDeferred();
    }

    /// <summary>The embedded window at <paramref name="point"/>, or null for the main window.</summary>
    private static Window WindowAt(Window root, Vector2 point) =>
        // Bottom to top.
        root.GetEmbeddedSubwindows()
            .Where(w => w.Visible)
            .LastOrDefault(w => Frame(w).HasPoint(point));

    /// <summary>Focuses the embedded window at <paramref name="point"/>, or the main window if there is none.</summary>
    private static void FocusWindowAt(Window root, Vector2 point)
    {
        var windows = root.GetEmbeddedSubwindows().Where(w => w.Visible).ToList();
        var under = WindowAt(root, point);
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
