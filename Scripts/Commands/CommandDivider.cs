using Godot;

/// <summary>
/// A dividing line in a <see cref="CommandMenu"/>.
/// Command menus merge adjacent dividers and trim dividers left at either end.
/// This just gives us a Godot Node so that we can add it in the editor.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://Textures/Editor/CommandDivider.svg")]
public partial class CommandDivider : Node
{
    public override string[] _GetConfigurationWarnings() => CommandMenuButton.OutsideWarnings(this);

    public override void _Notification(int what) => CommandMenuButton.WarnWhenMoved(this, what);
}
