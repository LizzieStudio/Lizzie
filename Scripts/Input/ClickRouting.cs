using System.Linq;
using Godot;

/// <summary>
/// <para>Makes clicks for right-click context menus behave as expected.</para>
///
/// <para>
/// A right-click anywhere opens the command menu for the view of the window it's on.
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

        if (!button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.Right)
                OpenMenu(root, button.Position);
            return;
        }

        // First, since closing the menu hands focus back to the window under it.
        CommandMenu.CloseUnlessOver(button.Position);

        if (button.ButtonIndex != MouseButton.Left)
            FocusWindowAt(root, button.Position);
    }

    /// <summary>
    /// Opens the menu of the focused window's view.
    /// </summary>
    private static void OpenMenu(Window root, Vector2 at)
    {
        // Nothing else can be used while a modal window is open.
        // Don't open a menu mid-drag or over a popup.
        // A captured mouse is steering something, like the camera's rotation, so its release isn't a click.
        if (
            root.GetEmbeddedSubwindows().Any(w => w.Visible && w.Exclusive)
            || WindowAt(root, at) is Popup
            || ImGuiInterop.ClaimingMouse
            || Input.IsMouseButtonPressed(MouseButton.Left)
            || Input.MouseMode == Input.MouseModeEnum.Captured
        )
            return;

        var window = CommandViews.FocusedWindow(root);
        // A text box keeps the Godot native context menu.
        // This is similar to the strategy of applications like Google Sheets.
        if (window.GuiGetHoveredControl() is (LineEdit or TextEdit) and var box && box.HasFocus())
            return;

        if (CommandViews.Find(window) is not { } view)
            return;
        // Writes a text box being edited, so the menu acts on what it shows.
        window.GuiReleaseFocus();
        CommandMenu.Show((Vector2I)at, view);
    }

    /// <summary>The embedded window at <paramref name="point"/>, or null for the main window.</summary>
    private static Window WindowAt(Window root, Vector2 point) =>
        // Bottom to top.
        root.GetEmbeddedSubwindows()
            .Where(w => w.Visible)
            .LastOrDefault(w => Frame(w).HasPoint(point));

    /// <summary>
    /// Focuses the embedded window at <paramref name="point"/>, or the main window if there is none.
    /// </summary>
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
