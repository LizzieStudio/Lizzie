using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Godot;

/// <summary>
/// Every command, defined once, in the partial file for its domain.
/// Menus and keyboard shortcuts both run them from here.
/// </summary>
public static partial class CommandList
{
    private static IReadOnlyList<Command> _all;

    /// <summary>Every command, in menu order.</summary>
    // Built on first use: static fields in other partial files may not be set yet at type initialization.
    public static IReadOnlyList<Command> All =>
        _all ??= Checked(
        [
            FlipComponent,
            RotateComponentCw,
            RotateComponentCcw,
            RollDie,
            ShuffleDeck,
            DrawCards,
            DealCards,
            SetDieFace,
            MoveComponentToTop,
            MoveComponentToBottom,
            DuplicateComponent,
            DeleteComponent,
            ZoomToComponent,
            EditPrototype,
            Undo,
            Redo,
        ]);

    /// <summary>
    /// Reports any command that isn't in <see cref="All"/>, which nothing could run,
    /// and any id used by more than one command.
    /// </summary>
    private static IReadOnlyList<Command> Checked(IReadOnlyList<Command> all)
    {
        var listed = all.ToHashSet();
        foreach (var field in typeof(CommandList).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.GetValue(null) is Command command && !listed.Contains(command))
                GD.PushError(
                    $"CommandList.{field.Name} is not in CommandList.All, so no menu or shortcut can run it."
                );

        foreach (var id in all.GroupBy(c => c.Id).Where(g => g.Count() > 1))
            GD.PushError($"Command id \"{id.Key.Id}\" is used by {id.Count()} commands.");

        return all;
    }

    /// <summary>
    /// The commands a menu offers, with what each acts on.
    /// A command shows when it can act on every target of its kind, primary or secondary.
    /// </summary>
    public static IEnumerable<(Command Command, IReadOnlyList<Target> Targets)> ForMenu(
        CommandContext context
    )
    {
        var all = context.Primary.Union(context.Secondary);
        foreach (var command in All.Where(c => c.ShowInMenu))
        {
            if (command.Count == TargetCount.None)
            {
                yield return (command, []);
                continue;
            }

            var targets = command.OfKind(all).ToList();
            if (Fits(command, targets.Count) && targets.All(command.Applies))
                yield return (command, targets);
        }
    }

    /// <summary>
    /// Runs the commands the key is bound to, on the primary targets they can act on.
    /// With no context, only commands without targets run.
    /// True when the key belongs to a command, even if there was nothing to act on.
    /// </summary>
    public static bool RunShortcut(InputEvent e, CommandContext context)
    {
        bool matched = false;
        foreach (var command in All)
        {
            if (context == null && command.Count != TargetCount.None)
                continue;
            if (!command.Matches(e, out int quantity))
                continue;
            matched = true;

            var targets =
                context == null
                    ? []
                    : command.OfKind(context.Primary).Where(command.Applies).ToList();
            if (command.Count == TargetCount.None || Fits(command, targets.Count))
                command.Run(targets, quantity);
        }
        return matched;
    }

    private static bool Fits(Command command, int count) =>
        command.Count switch
        {
            TargetCount.One => count == 1,
            TargetCount.Many => count > 0,
            _ => count == 0,
        };
}
