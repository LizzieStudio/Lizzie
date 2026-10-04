using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Decides which commands apply and what they act on, for menus, buttons and keyboard shortcuts.
/// </summary>
public static class CommandResolver
{
    /// <summary>
    /// The commands a context menu offers: <see cref="CommandList.ContextMenu"/> with the view's own commands.
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
        CommandList
            .All.Concat(context.Local)
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
        var order = CommandList.ContextMenu.ToList();
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
