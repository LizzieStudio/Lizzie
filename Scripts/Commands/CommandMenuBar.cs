using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <para>The menus along the top of the main window.</para>
///
/// The commands run in the window that has focus.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://Textures/Editor/CommandMenuBar.svg")]
public partial class CommandMenuBar : HBoxContainer
{
    /// <summary>
    /// The focused view where the commands run.
    /// </summary>
    public ICommandView View { get; private set; }

    private IEnumerable<CommandMenuButton> Titles => GetChildren().OfType<CommandMenuButton>();

    public override void _Ready()
    {
        if (!Engine.IsEditorHint())
            AddThemeConstantOverride("separation", GetThemeConstant("h_separation", "MenuBar"));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationChildOrderChanged && Engine.IsEditorHint() && IsInsideTree())
            UpdateConfigurationWarnings();
    }

    public override string[] _GetConfigurationWarnings() =>
        [
            .. GetChildren()
                .Where(c => c is not CommandMenuButton)
                .Select(c =>
                    $"{c.Name} isn't a CommandMenuButton, so it isn't one of the bar's menus."
                ),
        ];

    public override void _EnterTree()
    {
        if (!Engine.IsEditorHint())
            GetTree().Root.WindowInput += OnWindowInput;
    }

    public override void _ExitTree()
    {
        if (!Engine.IsEditorHint())
            GetTree().Root.WindowInput -= OnWindowInput;
    }

    // Sees the main window's input before the GUI, or any embedded window, does.
    private void OnWindowInput(InputEvent e)
    {
        switch (e)
        {
            // Before the press moves focus to the main window.
            // A press that just closed a bar menu keeps that menu's view.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press
                when !Titles.Any(t => t.IsOpen || t.ClosedThisFrame)
                    && TitleAt(press.Position) != null:
                View = FocusedView();
                break;

            // Moving onto another title while a menu is open opens that title's menu.
            case InputEventMouseMotion motion
                when Titles.FirstOrDefault(t => t.IsOpen) is { } open && CommandMenu.IsOpen:
                var title = TitleAt(motion.Position);
                if (title != null && title != open)
                    title.Open();
                break;
        }
    }

    private CommandMenuButton TitleAt(Vector2 at) =>
        Titles.FirstOrDefault(t => t.Visible && t.GetGlobalRect().HasPoint(at));

    // The view of the focused window, the table's when it's the main window.
    // Popups, like the menu itself, aren't where commands run.
    private ICommandView FocusedView()
    {
        var root = GetTree().Root;
        var focused = root.GetEmbeddedSubwindows()
            .FirstOrDefault(w => w.Visible && w.HasFocus() && w is not Popup);
        return CommandViews.Find(focused ?? root);
    }
}
