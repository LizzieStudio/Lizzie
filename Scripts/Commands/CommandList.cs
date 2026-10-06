using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Lizzie.Replication.Machinery;

/// <summary>
/// Every command, each defined once in a class for what it acts on, like <see cref="DeckCommands"/>.
/// Menus and keyboard shortcuts both run them from here, through <see cref="Lizzie.Replication.Machinery.CommandResolver"/>.
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
        UI.ChangeSeat,
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
                field.GetValue(null) is Command command
                && command != Command.Divider
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
        for (int n = 1; n <= ShortcutActions.NumberCount; n++)
        {
            if (OwnActions.Add(ShortcutActions.Number(n)))
                ShortcutActions.AddAction(ShortcutActions.Number(n), ShortcutActions.NumberKeys(n));
        }

        foreach (var command in commands.Where(c => c.Keys.Count > 0 && !Registered.Contains(c)))
        {
            ReportClashes(command);
            ShortcutActions.AddAction(command.Action, command.Keys);
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
}
