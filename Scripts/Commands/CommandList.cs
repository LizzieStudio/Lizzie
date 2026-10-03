using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

/// <summary>
/// Every command, each defined once in a class for what it acts on, like <see cref="DeckCommands"/>.
/// Menus and keyboard shortcuts both run them from here.
/// </summary>
public static class CommandList
{
    /// <summary>
    /// The <see cref="Command"/> every context menu can offer, in order.
    /// </summary>
    /// <remarks>
    /// Add a <see cref="Command"/> here to see it in the context menus.
    /// </remarks>
    public static readonly IReadOnlyList<Command> ContextMenu =
    [
        ComponentCommands.Flip,
        ComponentCommands.RotateCw,
        ComponentCommands.RotateCcw,
        DieCommands.Roll,
        DeckCommands.Shuffle,
        DeckCommands.Draw,
        DeckCommands.Deal,
        DieCommands.SetFace,
        ComponentCommands.MoveToTop,
        ComponentCommands.MoveToBottom,
        ComponentCommands.Delete,
        UndoCommands.UndoComponentChanges,
        PrototypeCommands.Edit,
        DataSetCommands.EditRow,
        DataSetCommands.DeleteRow,
        DataSetCommands.DeleteColumn,
        DataSetCommands.ClearCells,
        DataSetCommands.CutCells,
        DataSetCommands.CopyCells,
        DataSetCommands.PasteCells,
        UndoCommands.Undo,
        UndoCommands.Redo,
        UndoCommands.UndoOthers,
        UndoCommands.RedoOthers,
    ];

    /// <summary>
    /// Every command.
    /// </summary>
    /// <remarks>
    /// Add a <see cref="Command"/> here (but not in <see cref="ContextMenu"/>) if you don't want it in context menus but do want it in:
    /// <list type="bullet">
    /// <item>keyboard shortcut</item>
    /// <item>a CommandButton</item>
    /// <item>the Menu Bar</item>
    /// </list>
    /// You still have to add it to those locations, but this makes it possible without it showing up in context menus.
    /// </remarks>
    public static readonly IReadOnlyList<Command> All = Checked([
        .. ContextMenu,
        UI.OpenProjectManager,
        UI.OpenProject,
        UI.SaveProject,
        UI.CreateSnapshot,
        UI.UpdateSnapshot,
        UI.RestoreSnapshot,
        UI.ManageSnapshots,
        UI.OpenMultiplayer,
        UI.EditTemplates,
        UI.EditDatasets,
        UI.EditPrototypes,
        UI.EditImages,
        UI.EditProjectSettings,
        UI.InsertExistingComponent,
        UI.InsertNewComponent,
        DataSetCommands.AddRow,
        DataSetCommands.AddColumn,
    ]);

    /// <summary>
    /// Reports any command that isn't in <see cref="All"/>, which nothing could run,
    /// and any name used by more than one command.
    /// A command in a public static field of any command type is expected in <see cref="All"/>;
    /// a view's own commands are private or built per instance.
    /// </summary>
    private static IReadOnlyList<Command> Checked(IReadOnlyList<Command> all)
    {
        var listed = all.ToHashSet();
        var fields = typeof(CommandList)
            .Assembly.GetTypes()
            .Where(t => !t.ContainsGenericParameters)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => typeof(Command).IsAssignableFrom(f.FieldType));
        foreach (var field in fields)
            if (
                field.GetValue(null) is Command command and not DividerCommand
                && !listed.Contains(command)
            )
                GD.PushError(
                    $"{field.DeclaringType.Name}.{field.Name} is not in CommandList.All, so no menu or shortcut can run it."
                );

        foreach (var name in all.GroupBy(c => c.Name).Where(g => g.Count() > 1))
            GD.PushError($"Command name \"{name.Key.Value}\" is used by {name.Count()} commands.");

        return all;
    }

    /// <summary>The command in <see cref="All"/> named <paramref name="name"/>, or null.</summary>
    public static Command Named(string name) => All.FirstOrDefault(c => c.Name.Value == name);

    /// <summary>
    /// The names of the commands in <see cref="All"/> that <paramref name="include"/> picks,
    /// sorted, with an empty first choice, for a dropdown in the Inspector.
    /// This is specifically for the Godot Editor.
    /// </summary>
    public static string NameHint(System.Func<Command, bool> include) =>
        string.Join(",", All.Where(include).Select(c => c.Name.Value).Order().Prepend(""));

    // the actions this adds to the InputMap, and the commands they belong to
    private static readonly HashSet<StringName> OwnActions = [];
    private static readonly List<Command> Registered = [];

    /// <summary>
    /// Adds each command's keys to the <see cref="InputMap"/> as an action named by its name,
    /// and the number keys once. Views call it for the commands they offer themselves.
    /// Reports a key that would run two commands on the same kind of target,
    /// and a command key that another input action in the project also uses.
    /// </summary>
    public static void Register(IEnumerable<Command> commands)
    {
        for (int n = 1; n <= Shortcuts.NumberCount; n++)
        {
            if (OwnActions.Add(Shortcuts.Number(n)))
                Shortcuts.AddAction(Shortcuts.Number(n), Shortcuts.NumberKeys(n));
        }

        foreach (var command in commands.Where(c => c.Keys.Count > 0 && !Registered.Contains(c)))
        {
            ReportClashes(command);
            Shortcuts.AddAction(command.Action, command.Keys);
            OwnActions.Add(command.Action);
            Registered.Add(command);
        }
    }

    private static void ReportClashes(Command command)
    {
        foreach (var key in command.Keys)
        {
            // Commands on different kinds of target may share a key: they only clash when both
            // have targets, and then neither runs. The number keys are shared on purpose and aren't in Keys.
            foreach (var other in Registered.Where(o => o.GetType() == command.GetType()))
            {
                if (other.Keys.Any(k => k.IsMatch(key)))
                    GD.PushError(
                        $"Commands {other.Name.Value} and {command.Name.Value} both use {key.AsText()} on the same kind of target."
                    );
            }

            // Godot's own ui_ actions belong to text boxes and buttons, which take keys first.
            foreach (var action in InputMap.GetActions())
            {
                if (OwnActions.Contains(action) || action.ToString().StartsWith("ui_"))
                    continue;
                if (InputMap.ActionGetEvents(action).Any(e => e.IsMatch(key)))
                    GD.PushError(
                        $"Command {command.Name.Value} uses {key.AsText()}, which the input action {action} also uses."
                    );
            }
        }
    }

    /// <summary>
    /// The commands a context menu offers: <see cref="ContextMenu"/> with the view's own commands.
    /// </summary>
    public static IReadOnlyList<Command> ForContextMenu(CommandContext context) =>
        MenuOrder(context.Local);

    /// <summary>
    /// The items a menu of <paramref name="commands"/> shows, with what each acts on.
    /// Commands have properties which decide when they can act on a certain target.
    /// The commands inside of a Submenu are checked and that status is inherited by the submenu.
    /// </summary>
    /// <param name="all">
    /// Keeps all the commands, even when disabled.
    /// This is specifically for the Menu Bar, which shouldn't change.
    /// </param>
    public static List<(Command Command, IReadOnlyList<Target> Targets)> ForMenu(
        CommandContext context,
        IEnumerable<Command> commands,
        bool all = false
    )
    {
        var items = new List<(Command Command, IReadOnlyList<Target> Targets)>();
        foreach (var command in commands)
        {
            if (command == Command.Divider)
            {
                if (items.Count > 0 && items[^1].Command != Command.Divider)
                    items.Add((command, []));
                continue;
            }
            if (!command.ShowInMenu)
                continue;
            if (command is Submenu submenu)
            {
                if (all || ForMenu(context, submenu.ItemsFor(RecordService.Instance)).Count > 0)
                    items.Add((command, []));
                continue;
            }

            // A global command applies to no target, so it fits with none and always shows.
            var targets = TargetsFor(command, context, RecordService.Instance, menu: true);
            if (all || command.Fits(targets.Count))
                items.Add((command, targets));
        }
        if (items.Count > 0 && items[^1].Command == Command.Divider)
            items.RemoveAt(items.Count - 1);
        return items;
    }

    /// <summary>
    /// Runs the command the key is bound to, on the targets in <paramref name="view"/> it can act on.
    /// When several commands bound to the same key have targets in the context, none of them run.
    /// With nothing selected, they act on what's hovered. Without a view, only commands without targets run.
    /// True when the key belongs to a command, even if it didn't run.
    /// </summary>
    public static bool RunShortcut(InputEvent e, ICommandView view)
    {
        var context = CommandContext.Of(view, RecordService.Instance, keys: true);
        var bound = ForKeys(context).Where(k => k.Command.Matches(e, out _)).ToList();
        var ready = bound.Where(k => k.Command.Fits(k.Targets.Count)).ToList();
        if (ready.Count == 1 && ready[0].Command.IsAvailable(RecordService.Instance))
        {
            var (command, targets) = ready[0];
            command.Matches(e, out int number);
            command.Run(targets, number, view);
        }
        return bound.Count > 0;
    }

    /// <summary>
    /// Whether the command's keys would run it in <paramref name="context"/>, using the same logic as <see cref="RunShortcut"/>.
    /// Menus use this to decide if they should show a command's shortcut.
    /// </summary>
    public static bool RunsFromKeys(Command command, CommandContext context) =>
        ForKeys(context)
            .Where(k => SharesKey(k.Command, command) && k.Command.Fits(k.Targets.Count))
            .Select(k => k.Command)
            .SequenceEqual([command]);

    /// <summary>
    /// Every command that keyboard shortcuts could run in <paramref name="context"/>,
    /// with the targets those shortcuts act on.
    /// </summary>
    private static IEnumerable<(Command Command, List<Target> Targets)> ForKeys(
        CommandContext context
    ) =>
        All.Concat(context.Local)
            .Select(c => (c, TargetsFor(c, context, RecordService.Instance, menu: false)));

    /// <summary>
    /// Whether both commands share a keyboard shortcut.
    /// </summary>
    private static bool SharesKey(Command a, Command b) =>
        a == b
        || (a.NumberKeys && b.NumberKeys)
        || KeysOf(a).Any(k => KeysOf(b).Any(o => k.IsMatch(o)));

    private static IEnumerable<InputEvent> KeysOf(Command command) =>
        command.Action != null && InputMap.HasAction(command.Action)
            ? InputMap.ActionGetEvents(command.Action)
            : [];

    /// <summary>
    /// <para>The targets for this command.</para>
    /// Takes the parts of the context in <see cref="Command.ActsOn"/>
    /// and filters it by those that return true when passed to <see cref="Command.Applies"/>.
    /// </summary>
    /// <param name="R">What <see cref="Command.Applies"/> reads through, like a Watch's reader.</param>
    public static List<Target> TargetsFor(
        Command command,
        CommandContext context,
        IRecordReader R,
        bool menu
    )
    {
        var acts = command.ActsOn;
        var targets = acts.HasFlag(Context.Selected) ? context.Selected : [];
        if (acts.HasFlag(Context.Contents))
            targets = targets.Union(context.Contents);
        if (menu && acts.HasFlag(Context.Containers))
            targets = targets.Union(context.Containers);
        if (menu && acts.HasFlag(Context.Referenced))
            targets = targets.Union(context.Referenced);
        if (acts.HasFlag(Context.View))
            targets = targets.Union(context.View);
        return targets.Where(t => command.Applies(R, t)).ToList();
    }

    /// <summary>
    /// The context menu's commands in order.
    /// </summary>
    private static List<Command> MenuOrder(IReadOnlyList<Command> local)
    {
        var order = ContextMenu.ToList();
        int previous = -1;
        foreach (var command in local)
        {
            int last = order.FindLastIndex(c => c.GetType() == command.GetType());
            int firstGlobal = order.FindIndex(c => c is GlobalCommand);
            previous =
                command == Command.Divider ? previous + 1
                : last >= 0 ? last + 1
                : firstGlobal >= 0 ? firstGlobal
                : order.Count;
            order.Insert(previous, command);
        }

        for (int i = order.Count - 1; i > 0; i--)
        {
            var (before, after) = (order[i - 1], order[i]);
            if (
                before != Command.Divider
                && after != Command.Divider
                && before.GetType() != after.GetType()
            )
                order.Insert(i, Command.Divider);
        }
        return order;
    }
}
