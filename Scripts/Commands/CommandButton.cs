using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

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
public partial class CommandButton : Button
{
    // Icons are drawn at text size, as in menus.
    private const int IconSize = 20;

    private static readonly HashSet<CommandButton> InTree = [];

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
            if (!value && Edited())
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
            if (!value && Edited())
                Icon = null;
        }
    } = true;

    /// <summary>
    /// An override to set what it always acts on instead of using the context.
    /// </summary>
    public Func<IEnumerable<Target>> Targets { get; set; }

    /// <summary>
    /// Updates every command button in <paramref name="view"/>.
    /// </summary>
    public static void Refresh(ICommandView view)
    {
        foreach (var button in InTree)
            if (CommandViews.Of(button) == view)
                button.Refresh();
    }

    // The editor keeps nothing in static fields, so it can reload the game's code after a build.
    public override void _EnterTree()
    {
        if (!Engine.IsEditorHint())
            InTree.Add(this);
    }

    public override void _ExitTree() => InTree.Remove(this);

    public override void _Ready()
    {
        AddThemeConstantOverride("icon_max_width", IconSize);
        if (Engine.IsEditorHint())
        {
            Preview();
            return;
        }
        Pressed += Run;
        Refresh();
    }

    private void Refresh()
    {
        if (Command == null)
            return;

        var (_, targets) = Find();
        Disabled = !Command.Fits(targets.Count) || !Command.IsAvailable();
        if (ShowIcon)
            Icon = IconOf(Command);
        if (_sizedFor != Command)
            ReserveWidth();

        var label = Command.Label(targets.Count);
        if (ShowCaption)
            Text = label;

        // The key shows even while it's disabled, so it doesn't come and go with the selection.
        // The caption is only needed when the button doesn't show it.
        var key = Command.ShortcutLabel()?.GetAsText();
        TooltipText =
            ShowCaption ? key ?? ""
            : key == null ? label
            : $"{label} - {key}";
    }

    /// <summary>
    /// Makes it wide enough for a count of up to two digits,
    /// so the layout (usually) doesn't move as the count changes.
    /// </summary>
    private void ReserveWidth()
    {
        _sizedFor = Command;
        if (!ShowCaption || !Command.Caption.Contains("{0}"))
            return;

        Text = Command.Label(88);
        CustomMinimumSize = CustomMinimumSize with { X = GetMinimumSize().X };
    }

    private void Run()
    {
        var (view, targets) = Find();
        if (Command != null && Command.Fits(targets.Count) && Command.IsAvailable())
            Command.Run(targets, 1, view);
    }

    // Its view, and what the command would act on there.
    private (ICommandView View, List<Target> Targets) Find()
    {
        var view = CommandViews.Of(this);
        var context = CommandContext.Of(view);
        var targets =
            Targets != null ? Targets().Where(Command.Applies).ToList()
            : context != null ? CommandList.TargetsFor(Command, context, menu: true)
            : [];
        return (view, targets);
    }

    // A view's own commands are only known once the button is in it.
    private Command Named(string name) =>
        name == ""
            ? null
            : CommandList.All.FirstOrDefault(c => c.Name.Value == name)
                ?? (
                    IsInsideTree() && !Engine.IsEditorHint()
                        ? CommandViews.Of(this)?.Commands?.FirstOrDefault(c => c.Name.Value == name)
                        : null
                );

    private static Texture2D IconOf(Command command) =>
        command.Icon == null ? null : GD.Load<Texture2D>(command.Icon);

    #region Editor

    // After a change in the Inspector, shows the button as it will look and updates the Inspector.
    // False while the scene loads, or in the game.
    private bool Edited()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return false;
        Preview();
        NotifyPropertyListChanged();
        UpdateConfigurationWarnings();
        return true;
    }

    // Set up its placeholder for the Godot Editor.
    private void Preview()
    {
        var command = Command;
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
            property["hint_string"] = string.Join(
                ",",
                CommandList.All.Select(c => c.Name.Value).Order().Prepend("")
            );
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
