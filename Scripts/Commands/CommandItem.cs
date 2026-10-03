using System.Linq;
using System.Text.RegularExpressions;
using Godot;

/// <summary>
/// <para>An item in the menu of a <see cref="CommandMenuButton"/>.</para>
///
/// A <see cref="Submenu"/>, like Restore Snapshot, shows as a submenu.
/// In the editor it names itself after its command, like "ProjectSave", unless you rename it.
/// </summary>
/// <remarks>
/// To place this node in the Godot Editor:
/// <list type="number">
/// <item>Add a <see cref="CommandMenuBar"/> Node in a 2D scene.</item>
/// <item>Inside that, add a <see cref="CommandMenuButton"/> Node.</item>
/// <item>Inside that, add a <see cref="CommandItem"/> Node.</item>
/// <item>Set the <see cref="CommandItem"/>'s Command Name.</item>
/// </list>
/// </remarks>
[Tool]
[GlobalClass]
[Icon("res://Textures/Editor/CommandItem.svg")]
public partial class CommandItem : Node
{
    /// <summary>
    /// The name of the command it offers, like "project.save", picked from a list in the Godot Inspector.
    /// Any command in <see cref="CommandList.All"/>.
    /// </summary>
    [Export]
    public string CommandName
    {
        get;
        set
        {
            var old = field;
            field = value ?? "";
            if (!Engine.IsEditorHint() || !IsInsideTree())
                return;
            if (field != "" && IsAutoNamed(Name, old))
                Name = NodeName(field);
            UpdateConfigurationWarnings();
        }
    } = "";

    /// <summary>
    /// The name used for the Node in the Godot Inspector: "project.save" is "ProjectSave".
    /// </summary>
    internal static string NodeName(string command) =>
        string.Concat(
            command
                .Split('.', '_')
                .Where(part => part != "")
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );

    /// <summary>
    /// Whether <paramref name="name"/> was given by Godot ("CommandItem2") or by this node for
    /// <paramref name="command"/> ("ProjectSave"), rather than typed by a developer.
    /// Godot adds a number to a name a sibling already has.
    /// </summary>
    internal static bool IsAutoNamed(string name, string command) =>
        Numbered(name, nameof(CommandItem)) || command != "" && Numbered(name, NodeName(command));

    private static bool Numbered(string name, string stem) =>
        Regex.IsMatch(name, $"^{Regex.Escape(stem)}([ _-]?[0-9]+)?$");

    /// <summary>
    /// The command it offers, or null when none is named.
    /// </summary>
    public Command Command => CommandList.Named(CommandName);

    /// <summary>
    /// Lists the commands to pick from.
    /// </summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsStringName() != PropertyName.CommandName)
            return;
        property["hint"] = (int)PropertyHint.Enum;
        property["hint_string"] = CommandList.NameHint(_ => true);
    }

    public override string[] _GetConfigurationWarnings() =>
        [
            .. CommandName == "" ? ["Pick the command it offers in CommandName."]
            : Command == null ? [$"No command is named \"{CommandName}\" in CommandList.All."]
            : (string[])[],
            .. CommandMenuButton.OutsideWarnings(this),
        ];

    public override void _Notification(int what) => CommandMenuButton.WarnWhenMoved(this, what);
}
