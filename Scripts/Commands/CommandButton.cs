using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// <para>A button that runs a <see cref="Command"/> the way the context menu would.</para>
///
/// <para>
/// It uses the command's icon, label, and shortcut, and is disabled while the command can't run.
/// </para>
/// </summary>
/// <remarks>
/// You can place this using Godot's Editor.
/// In a 2D scene, add a CommandButton and set its command in the Inspector.
/// </remarks>
[Tool]
[GlobalClass]
[Icon("res://Textures/Editor/CommandButton.svg")]
public partial class CommandButton : Button
{
    // Icons are drawn at text size, as in menus.
    private const int IconSize = 20;

    // The command that we used when measuring its width.
    private Command _sizedFor;

    private Command _command;
    private string _commandName = "";

    /// <summary>The command it runs.</summary>
    public Command Command
    {
        get => _command ??= Named(_commandName);
        set
        {
            _command = value;
            _commandName = value?.Name.Value ?? "";
            Edited();
        }
    }

    /// <summary>
    /// The name of the command it runs, like "dataset.delete_row", picked from a list in the Inspector.
    /// Any command in <see cref="CommandList.All"/> or its view's own commands.
    /// </summary>
    [Export]
    public string CommandName
    {
        get => _commandName;
        set
        {
            _commandName = value ?? "";
            _command = null;
            Edited();
        }
    }

    /// <summary>
    /// Show the command's caption as a label.
    /// </summary>
    [Export]
    public bool ShowCaption
    {
        get;
        set
        {
            field = value;
            if (Edited() && !value)
                Text = "";
        }
    } = true;

    /// <summary>
    /// Show the command's icon.
    /// </summary>
    [Export]
    public bool ShowIcon
    {
        get;
        set
        {
            field = value;
            if (Edited() && !value)
                Icon = null;
        }
    } = true;

    public override void _EnterTree()
    {
        // The editor puts a deleted button back on undo without readying it again.
        if (Engine.IsEditorHint())
            Preview();
        else
            Atoms.R.Watch(this, Sync);
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("icon_max_width", IconSize);
        if (!Engine.IsEditorHint())
            Pressed += Run;
    }

    private void Sync(IWatchReader R)
    {
        if (Command == null)
            return;

        var context = CommandContext.Of(CommandViews.Of(this), R);
        var (command, targets, blocked) = CommandResolver.Resolve(Command, context, R, menu: true);
        Disabled = !command.Fits(targets.Count) || !command.IsAvailable(R);
        if (ShowIcon)
            Icon = IconOf(command);
        if (_sizedFor != command)
            ReserveWidth(command);

        var label = command.Label(targets.Count);
        if (ShowCaption)
            Text = label;

        // The key shows even while it's disabled, so it doesn't come and go with the selection.
        // The caption is only needed when the button doesn't show it.
        // The command's own tooltip, and why it's disabled, go below on their own lines.
        var key = command.ShortcutLabel()?.GetAsText();
        var heading =
            ShowCaption ? key
            : key == null ? label
            : $"{label} ({key})";
        var reason = Disabled ? command.DisabledReason(R, targets.Count, blocked) : null;
        TooltipText = string.Join(
            "\n",
            new[] { heading, command.TooltipWith(reason) }.Where(s => s != null)
        );
    }

    /// <summary>
    /// Makes it wide enough for a count of up to two digits,
    /// so the layout (usually) doesn't move as the count changes.
    /// </summary>
    private void ReserveWidth(Command command)
    {
        _sizedFor = command;
        if (!ShowCaption || !command.Caption.Contains("{0}"))
            return;

        Text = command.Label(88);
        CustomMinimumSize = CustomMinimumSize with { X = GetMinimumSize().X };
    }

    private void Run()
    {
        if (Command == null)
            return;
        var view = CommandViews.Of(this);
        var (command, targets, _) = CommandResolver.Resolve(
            Command,
            CommandContext.Of(view, Atoms.R),
            Atoms.R,
            menu: true
        );
        command.Run(targets, view);
    }

    // A view's own commands are only known once the button is in it.
    private Command Named(string name) =>
        name == ""
            ? null
            : CommandList.Named(name)
                ?? (
                    IsInsideTree() && !Engine.IsEditorHint()
                        ? CommandViews.Of(this)?.Commands?.FirstOrDefault(c => c.Name.Value == name)
                        : null
                );

    private static Texture2D IconOf(Command command) =>
        command.Icon == null ? null : GD.Load<Texture2D>(command.Icon);

    #region Editor

    /// <summary>
    /// After a change in the Inspector, shows the button as it will look and updates the Inspector.
    /// </summary>
    /// <returns>False while the scene loads, or in the game, where it updates automatically.</returns>
    private bool Edited()
    {
        if (!IsInsideTree())
            return false;
        if (!Engine.IsEditorHint())
        {
            Atoms.R.QueueSync(this);
            return false;
        }
        Preview();
        NotifyPropertyListChanged();
        UpdateConfigurationWarnings();
        return true;
    }

    // Set up its placeholder for the Godot Editor.
    private void Preview()
    {
        var command = Command is FirstOfCommand first ? first.Members[0] : Command;
        if (ShowCaption)
            Text = command?.Label(99) ?? "";
        if (ShowIcon)
            Icon = command == null ? null : IconOf(command);
    }

    /// <summary>
    /// Lists the commands to pick from.
    /// </summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        if (name == PropertyName.CommandName)
        {
            property["hint"] = (int)PropertyHint.Enum;
            // A button has nowhere to show choices.
            property["hint_string"] = CommandList.NameHint(c => c is not IChoiceCommand);
        }
        else if (
            name == Button.PropertyName.TooltipText
            || name == Button.PropertyName.Text && ShowCaption
            || name == Button.PropertyName.Icon && ShowIcon
        )
        {
            var usage = (PropertyUsageFlags)property["usage"].AsInt64();
            property["usage"] = (long)(
                usage & ~PropertyUsageFlags.Storage | PropertyUsageFlags.ReadOnly
            );
        }
    }

    public override string[] _GetConfigurationWarnings() =>
        _commandName == "" ? ["Pick the command it runs in CommandName."]
        : Command == null
            ?
            [
                $"No command is named \"{_commandName}\" in CommandList.All. "
                    + "A view's own commands are only found when the game runs.",
            ]
        : [];

    #endregion
}
