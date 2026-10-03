using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <para>A button that opens a menu of the <see cref="CommandItem"/>s and <see cref="CommandDivider"/>s under it.</para>
///
/// Its text is the menu's title. In a <see cref="CommandMenuBar"/>, it's one of the bar's menus.
/// Elsewhere, like a dropdown in a view's toolbar, its commands run in its own view.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://Textures/Editor/CommandMenuButton.svg")]
public partial class CommandMenuButton : Button
{
    private ulong _closedFrame = ulong.MaxValue;

    /// <summary>
    /// Whether the menu is showing.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Whether its menu closed this frame, as a press on the button does before it's pressed.
    /// </summary>
    public bool ClosedThisFrame => _closedFrame == Engine.GetProcessFrames();

    /// <summary>
    /// The menu contents in order, command and dividers.
    /// </summary>
    public IEnumerable<Command> Items() =>
        GetChildren()
            .Select(c =>
                c switch
                {
                    CommandItem item => item.Command,
                    CommandDivider => Command.Divider,
                    _ => null,
                }
            )
            .Where(c => c != null);

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;

        ToggleMode = true;
        ActionMode = ActionModeEnum.Press;
        FocusMode = FocusModeEnum.None;
        Pressed += () =>
        {
            // The press already closed its menu.
            if (ClosedThisFrame)
                SetPressedNoSignal(false);
            else
                Open();
        };
    }

    /// <summary>
    /// Opens the menu.
    /// </summary>
    public void Open()
    {
        var view = GetParent() is CommandMenuBar bar ? bar.View : CommandViews.Of(this);
        var rect = GetGlobalRect();
        CommandMenu.Show(
            (Vector2I)(rect.Position + new Vector2(0, rect.Size.Y)),
            view,
            Items(),
            closed: Closed
        );
        IsOpen = true;
        SetPressedNoSignal(true);
    }

    private void Closed()
    {
        IsOpen = false;
        _closedFrame = Engine.GetProcessFrames();
        SetPressedNoSignal(false);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationChildOrderChanged && Engine.IsEditorHint() && IsInsideTree())
            UpdateConfigurationWarnings();
    }

    public override string[] _GetConfigurationWarnings()
    {
        var children = GetChildren();
        var warnings = new List<string>();
        if (!children.Any(c => c is CommandItem))
            warnings.Add("Add CommandItems under it for its menu to offer.");
        foreach (var other in children.Where(c => c is not (CommandItem or CommandDivider)))
            warnings.Add(
                $"{other.Name} isn't a CommandItem or CommandDivider, so its menu leaves it out."
            );
        return [.. warnings];
    }

    /// <summary>
    /// The warning for a <see cref="CommandItem"/> or <see cref="CommandDivider"/> outside a menu button.
    /// </summary>
    internal static string[] OutsideWarnings(Node node) =>
        node.GetParent() is CommandMenuButton
            ? []
            : [$"It only shows in a menu when it's under a {nameof(CommandMenuButton)}."];

    /// <summary>
    /// Updates the warnings of a <see cref="CommandItem"/> or <see cref="CommandDivider"/>
    /// when it's added or moved in the editor.
    /// </summary>
    internal static void WarnWhenMoved(Node node, int what)
    {
        if (what == NotificationEnterTree && Engine.IsEditorHint())
            node.UpdateConfigurationWarnings();
    }
}
