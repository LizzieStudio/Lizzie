using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The menus along the top of the main window.
/// The commands run in the window that was focused when the title was pressed.
/// </summary>
public partial class CommandMenuBar : HBoxContainer
{
    private readonly List<(Button Title, Submenu Menu)> _titles = [];

    // The title whose menu is showing, or null.
    private Button _open;

    // Where the open menu's commands run.
    private ICommandView _view;

    private Button _closedTitle;
    private ulong _closedFrame;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", GetThemeConstant("h_separation", "MenuBar"));
        foreach (var menu in CommandList.MenuBar)
        {
            var title = new Button
            {
                Text = menu.Caption,
                ThemeTypeVariation = "MenuBar",
                ToggleMode = true,
                ActionMode = BaseButton.ActionModeEnum.Press,
                FocusMode = FocusModeEnum.None,
            };
            title.Pressed += () => OnTitlePressed(title, menu);
            AddChild(title);
            _titles.Add((title, menu));
        }
    }

    public override void _EnterTree() => GetTree().Root.WindowInput += OnWindowInput;

    public override void _ExitTree() => GetTree().Root.WindowInput -= OnWindowInput;

    // Sees the main window's input before the GUI, or any embedded window, does.
    private void OnWindowInput(InputEvent e)
    {
        switch (e)
        {
            // Before the press moves focus to the main window.
            // A press that just closed a bar menu keeps that menu's view.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press
                when _open == null
                    && _closedFrame != Engine.GetProcessFrames()
                    && TitleAt(press.Position) != null:
                _view = FocusedView();
                break;

            // Moving onto another title while a menu is open opens that title's menu.
            case InputEventMouseMotion motion when _open != null && CommandMenu.IsOpen:
                var (title, menu) = _titles.FirstOrDefault(t =>
                    t.Title == TitleAt(motion.Position)
                );
                if (title != null && title != _open)
                    Open(title, menu);
                break;
        }
    }

    private void OnTitlePressed(Button title, Submenu menu)
    {
        // The press already closed this title's menu.
        if (title == _closedTitle && _closedFrame == Engine.GetProcessFrames())
            ShowOpen();
        else
            Open(title, menu);
    }

    private void Open(Button title, Submenu menu)
    {
        _open = title;
        var rect = title.GetGlobalRect();
        CommandMenu.Show(
            (Vector2I)(rect.Position + new Vector2(0, rect.Size.Y)),
            _view,
            menu.Items(ProjectService.Instance),
            closed: () => Closed(title)
        );
        ShowOpen();
    }

    // Called when the title's menu closes, or another menu replaces it.
    private void Closed(Button title)
    {
        if (_open != title)
            return;
        _open = null;
        _closedTitle = title;
        _closedFrame = Engine.GetProcessFrames();
        ShowOpen();
    }

    // Only the open menu's title looks pressed.
    private void ShowOpen()
    {
        foreach (var (title, _) in _titles)
            title.SetPressedNoSignal(title == _open);
    }

    private Button TitleAt(Vector2 at) =>
        _titles.Select(t => t.Title).FirstOrDefault(t => t.GetGlobalRect().HasPoint(at));

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
