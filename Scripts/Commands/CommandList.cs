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
        PrototypeCommands.Edit,
        UndoCommands.Undo,
        UndoCommands.Redo,
    ]);

    /// <summary>
    /// Reports any command that isn't in <see cref="All"/>, which nothing could run,
    /// and any id used by more than one command.
    /// </summary>
    private static IReadOnlyList<Command> Checked(IReadOnlyList<Command> all)
    {
        var listed = all.ToHashSet();
        var fields = typeof(CommandList)
            .Assembly.GetTypes()
            .Where(t => !t.ContainsGenericParameters)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(Command));
        foreach (var field in fields)
            if (field.GetValue(null) is Command command && !listed.Contains(command))
                GD.PushError(
                    $"{field.DeclaringType.Name}.{field.Name} is not in CommandList.All, so no menu or shortcut can run it."
                );

        foreach (var id in all.GroupBy(c => c.Id).Where(g => g.Count() > 1))
            GD.PushError($"Command id \"{id.Key.Id}\" is used by {id.Count()} commands.");

        return all;
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
            var targets = TargetsFor(command, context, withReferenced: true);
            if (command.Fits(targets.Count))
                yield return (command, targets);
        }
    }

    /// <summary>
    /// Runs the commands the key is bound to, on the selected targets they can act on.
    /// With no context, only commands without targets run.
    /// True when the key belongs to a command, even if there was nothing to act on.
    /// </summary>
    public static bool RunShortcut(InputEvent e, CommandContext context)
    {
        bool matched = false;
        foreach (var command in All.Concat(context?.Local ?? []))
        {
            if (context == null && command is not GlobalCommand)
                continue;
            if (!command.Matches(e, out int number))
                continue;
            matched = true;

            var targets =
                context == null ? [] : TargetsFor(command, context, withReferenced: false);
            if (command.Fits(targets.Count))
                command.Run(targets, number);
        }
        return matched;
    }

    /// <summary>
    /// <para>The targets for this command.</para>
    /// <para>
    /// First we take the following context:
    /// <list type="bullet">
    /// <item>the selected targets always</item>
    /// <item>the contents if the command is configured to include them</item>
    /// <item>the referenced targets when asked (yes for the menu, no for the keyboard)</item>
    /// </list>
    /// </para>
    /// <para>Then we keep the ones the command applies to: <see cref="Command.Applies"/>.</para>
    /// </summary>
    private static List<Target> TargetsFor(
        Command command,
        CommandContext context,
        bool withReferenced
    )
    {
        var targets = context.Selected;
        if (command.IncludesContents)
            targets = targets.Union(context.Contents);
        if (withReferenced)
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
