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
    /// <summary>Every command, in menu order.</summary>
    public static readonly IReadOnlyList<Command> All = Checked([
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
    ]);

    /// <summary>
    /// Reports any command that isn't in <see cref="All"/>, which nothing could run,
    /// and any id used by more than one command.
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
            if (field.GetValue(null) is Command command && !listed.Contains(command))
                GD.PushError(
                    $"{field.DeclaringType.Name}.{field.Name} is not in CommandList.All, so no menu or shortcut can run it."
                );

        foreach (var id in all.GroupBy(c => c.Id).Where(g => g.Count() > 1))
            GD.PushError($"Command id \"{id.Key.Id}\" is used by {id.Count()} commands.");

        return all;
    }

    // the actions this adds to the InputMap, and the commands they belong to
    private static readonly HashSet<StringName> OwnActions = [];
    private static readonly List<Command> Registered = [];

    /// <summary>
    /// Adds each command's keys to the <see cref="InputMap"/> as an action named by its id,
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
            // Commands on different kinds of target may share a key, since each runs on its own
            // targets. The number keys are shared on purpose and aren't in Keys.
            foreach (var other in Registered.Where(o => o.GetType() == command.GetType()))
            {
                if (other.Keys.Any(k => k.IsMatch(key)))
                    GD.PushError(
                        $"Commands {other.Id.Id} and {command.Id.Id} both use {key.AsText()} on the same kind of target."
                    );
            }

            // Godot's own ui_ actions belong to text boxes and buttons, which take keys first.
            foreach (var action in InputMap.GetActions())
            {
                if (OwnActions.Contains(action) || action.ToString().StartsWith("ui_"))
                    continue;
                if (InputMap.ActionGetEvents(action).Any(e => e.IsMatch(key)))
                    GD.PushError(
                        $"Command {command.Id.Id} uses {key.AsText()}, which the input action {action} also uses."
                    );
            }
        }
    }

    /// <summary>
    /// The commands a menu offers, with what each acts on.
    /// A command shows when it can act on any of the targets, and acts on just those.
    /// </summary>
    public static IEnumerable<(Command Command, IReadOnlyList<Target> Targets)> ForMenu(
        CommandContext context
    )
    {
        foreach (var command in MenuOrder(context.Local).Where(c => c.ShowInMenu))
        {
            // A global command applies to no target, so it fits with none and always shows.
            var targets = TargetsFor(command, context, menu: true);
            if (command.Fits(targets.Count))
                yield return (command, targets);
        }
    }

    /// <summary>
    /// Runs the commands the key is bound to, on the targets in <paramref name="view"/> they can act on.
    /// Without a view, or while it isn't taking commands, only commands without targets run.
    /// True when the key belongs to a command, even if there was nothing to act on.
    /// </summary>
    public static bool RunShortcut(InputEvent e, ICommandView view)
    {
        var context = view?.BuildContext();
        bool matched = false;
        foreach (var command in All.Concat(context?.Local ?? []))
        {
            if (context == null && command is not GlobalCommand)
                continue;
            if (!command.Matches(e, out int number))
                continue;
            matched = true;

            var targets = context == null ? [] : TargetsFor(command, context, menu: false);
            if (command.Fits(targets.Count))
                command.Run(targets, number, view);
        }
        return matched;
    }

    /// <summary>
    /// Whether the command's keys would run it in <paramref name="context"/>, as <see cref="RunShortcut"/> decides:
    /// it must fit the targets keys act on, which leave out containers and referenced ones.
    /// Menus show a command's shortcut only then.
    /// </summary>
    public static bool RunsFromKeys(Command command, CommandContext context) =>
        command.Fits(TargetsFor(command, context, menu: false).Count);

    /// <summary>
    /// <para>The targets for this command.</para>
    /// Takes the parts of the context in <see cref="Command.ActsOn"/>
    /// and filters it by those that return true when passed to <see cref="Command.Applies"/>.
    /// </summary>
    private static List<Target> TargetsFor(Command command, CommandContext context, bool menu)
    {
        var acts = command.ActsOn;
        var targets = acts.HasFlag(Context.Selected) ? context.Selected : [];
        if (acts.HasFlag(Context.Contents))
            targets = targets.Union(context.Contents);
        if (menu && acts.HasFlag(Context.Containers))
            targets = targets.Union(context.Containers);
        if (menu && acts.HasFlag(Context.Referenced))
            targets = targets.Union(context.Referenced);
        return targets.Where(command.Applies).ToList();
    }

    /// <summary>
    /// Every command in menu order. A view's own commands go after the shared ones
    /// on the same kind of target, or before the ones without targets.
    /// </summary>
    private static List<Command> MenuOrder(IReadOnlyList<Command> local)
    {
        var order = All.ToList();
        foreach (var command in local)
        {
            int last = order.FindLastIndex(c => c.GetType() == command.GetType());
            int firstGlobal = order.FindIndex(c => c is GlobalCommand);
            order.Insert(
                last >= 0 ? last + 1
                    : firstGlobal >= 0 ? firstGlobal
                    : order.Count,
                command
            );
        }
        return order;
    }
}
