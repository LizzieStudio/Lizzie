using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// Utilities to create context menus and the menu bar in the top left.
/// </summary>
public static class CommandMenu
{
    // Icons are drawn at text size, whatever size they're made at.
    private const int IconSize = 20;

    private static PopupMenu _menu;

    // What the menu offers, by item id, and where it runs them.
    private static readonly List<(Command Command, IReadOnlyList<AtomRef> Targets)> Items = [];
    private static ICommandView _view;

    // Called once when what the menu shows is done with: dismissed, used, or replaced.
    private static Action _closed;

    /// <summary>
    /// Opens the context menu for what's targeted in <paramref name="view"/> at <paramref name="position"/>,
    /// taking the menu from wherever it was open.
    /// </summary>
    /// <param name="position">Where it opens, in the main window's coordinates.</param>
    /// <param name="view">Where the commands run, and what they act on.</param>
    /// <param name="closed">
    /// Called once when this menu closes or is replaced, before a chosen command runs, or null.
    /// </param>
    public static void Show(Vector2I position, ICommandView view, Action closed = null)
    {
        var context = CommandContext.Of(view, AtomService.Instance);
        Show(position, context, CommandResolver.ForContextMenu(context), view, closed);
    }

    /// <summary>
    /// Opens the menu with <paramref name="commands"/> at <paramref name="position"/>,
    /// acting on what's targeted in <paramref name="view"/>, taking the menu from wherever it was open.
    /// </summary>
    /// <param name="position">Where it opens, in the main window's coordinates.</param>
    /// <param name="view">Where the commands run, and what they act on, or null for neither.</param>
    /// <param name="commands">What it offers, in order, with dividers.</param>
    /// <param name="closed">Called when this menu closes without a command running.</param>
    public static void Show(
        Vector2I position,
        ICommandView view,
        IEnumerable<Command> commands,
        Action closed = null
    ) =>
        Show(position, CommandContext.Of(view, AtomService.Instance), commands, view, closed, true);

    /// <summary>
    /// Opens the menu with <paramref name="commands"/> at <paramref name="position"/>,
    /// taking the menu from wherever it was open.
    /// Each item shows its shortcut, and commands with choices get a submenu of them.
    /// </summary>
    /// <param name="position">Where it opens.</param>
    /// <param name="context">What the commands act on.</param>
    /// <param name="commands">Its potential contents.</param>
    /// <param name="view">Where the commands run.</param>
    /// <param name="closed">Called when this menu closes without a command running.</param>
    /// <param name="all">Shows the commands with nothing to act on as disabled.</param>
    private static void Show(
        Vector2I position,
        CommandContext context,
        IEnumerable<Command> commands,
        ICommandView view,
        Action closed,
        bool all = false
    )
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        var menu = Menu(CommandViews.FocusedWindow(root));
        menu.Hide();
        Done();

        menu.Clear(freeSubmenus: true);
        Items.Clear();
        _view = view;
        _closed = closed;
        Fill(menu, context, commands, all);

        Callable.From(() => Open(menu, position)).CallDeferred();
    }

    /// <summary>Whether the menu is showing.</summary>
    public static bool IsOpen => GodotObject.IsInstanceValid(_menu) && _menu.Visible;

    private static void Fill(
        PopupMenu menu,
        CommandContext context,
        IEnumerable<Command> commands,
        bool all
    )
    {
        IAtomReader R = AtomService.Instance;
        foreach (var (command, targets, blocked) in CommandResolver.ForMenu(context, commands, all))
        {
            if (command == Command.Divider)
            {
                menu.AddSeparator();
                continue;
            }

            // Ids skip the separators, so they index the items.
            int id = Items.Count;
            Items.Add((command, targets));
            menu.AddItem(command.Label(targets.Count), id);
            int index = menu.GetItemIndex(id);
            bool disabled = !command.Fits(targets.Count) || !command.IsAvailable(R);
            menu.SetItemDisabled(index, disabled);

            // A key shows only if pressing it here would run this command.
            bool keys = CommandResolver.RunsFromKeys(command, context);
            if (keys && command.ShortcutLabel() is { } shortcut)
                menu.SetItemShortcut(index, shortcut);

            if (command.Icon != null)
            {
                menu.SetItemIcon(index, GD.Load<Texture2D>(command.Icon));
                menu.SetItemIconMaxWidth(index, IconSize);
            }

            var reason = disabled ? command.DisabledReason(R, targets.Count, blocked) : null;
            if (command.TooltipWith(reason) is { } tooltip)
                menu.SetItemTooltip(index, tooltip);

            if (disabled)
                continue;

            if (command is IChoiceCommand choice)
                AddChoiceSubmenu(menu, choice, targets, index, keys);
        }
    }

    /// <summary>
    /// Closes the menu unless <paramref name="at"/>, in the main window's coordinates,
    /// is on it or on one of its submenus.
    /// </summary>
    public static void CloseUnlessOver(Vector2 at)
    {
        if (GodotObject.IsInstanceValid(_menu) && _menu.Visible && !IsOver(_menu, at))
            _menu.Hide();
    }

    private static bool IsOver(PopupMenu menu, Vector2 at) =>
        new Rect2(menu.Position, menu.Size).HasPoint(at)
        || menu.GetChildren().OfType<PopupMenu>().Any(sub => sub.Visible && IsOver(sub, at));

    private static void Open(PopupMenu menu, Vector2I position)
    {
        menu.Position = position;
        menu.ResetSize();
        menu.Popup();
    }

    private static PopupMenu Menu(Window window)
    {
        if (!GodotObject.IsInstanceValid(_menu))
        {
            _menu = new PopupMenu { Name = "CommandMenu" };
            _menu.PopupHide += Done;
            _menu.IdPressed += id =>
            {
                var (command, targets) = Items[(int)id];
                // The item of a command with choices only opens its submenu.
                if (command is not IChoiceCommand)
                    Run(command, targets);
            };
        }
        if (_menu.GetParent() != window)
        {
            _menu.GetParent()?.RemoveChild(_menu);
            window.AddChild(_menu);
        }
        return _menu;
    }

    // Tells whoever opened what the menu shows that it's done with.
    private static void Done()
    {
        var closed = _closed;
        _closed = null;
        closed?.Invoke();
    }

    // Done first, so a command can change what closing changes, like Duplicate entering spawn mode.
    private static void Run(Command command, IReadOnlyList<AtomRef> targets)
    {
        Done();
        command.Run(targets, _view);
    }

    private static void AddChoiceSubmenu(
        PopupMenu menu,
        IChoiceCommand command,
        IReadOnlyList<AtomRef> targets,
        int index,
        bool keys
    )
    {
        var sub = new PopupMenu { Name = $"ChoiceSubmenu_{index}" };
        var choices = command.ChoicesFor(targets);
        foreach (var (label, _) in choices)
        {
            sub.AddItem(label);
            // The number keys pick the first choices, so they show as their shortcuts.
            int n = sub.ItemCount;
            if (
                keys
                && command.NumberKeys
                && n <= ShortcutActions.NumberCount
                && ShortcutActions.Label(ShortcutActions.Number(n)) is { } shortcut
            )
                sub.SetItemShortcut(n - 1, shortcut);
        }

        // Done first, as for any other command.
        sub.IndexPressed += i =>
        {
            Done();
            command.Choose(targets, choices[(int)i].Value, _view);
        };
        menu.AddChild(sub);
        menu.SetItemSubmenuNode(index, sub);
    }
}
