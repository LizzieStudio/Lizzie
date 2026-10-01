using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Utilities to create context menus and the menu bar in the top left.
/// </summary>
public static class CommandMenu
{
    // Icons are drawn at text size, whatever size they're made at.
    private const int IconSize = 20;

    // The submenu offers the top row of number keys, 1 through 9, unless the command gives its own limit.
    private const int SubmenuNumbers = 9;

    // It never offers more numbers than there are number keys, even with a higher limit.
    private const int MaxSubmenuNumbers = 20;

    private static PopupMenu _menu;

    // What the menu offers, by item id, and where it runs them.
    private static readonly List<(Command Command, IReadOnlyList<Target> Targets)> Items = [];
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
        var context = CommandContext.Of(view) ?? new CommandContext();
        Show(position, context, CommandList.ForContextMenu(context), view, closed);
    }

    /// <summary>
    /// Opens the menu with <paramref name="commands"/> at <paramref name="position"/>,
    /// acting on what's targeted in <paramref name="view"/>, taking the menu from wherever it was open.
    /// </summary>
    /// <param name="position">Where it opens, in the main window's coordinates.</param>
    /// <param name="view">Where the commands run, and what they act on, or null for neither.</param>
    /// <param name="commands">What it offers, in order, with dividers and submenus.</param>
    /// <param name="closed">Called when this menu closes without a command running.</param>
    public static void Show(
        Vector2I position,
        ICommandView view,
        IEnumerable<Command> commands,
        Action closed = null
    ) => Show(position, CommandContext.Of(view) ?? new CommandContext(), commands, view, closed);

    /// <summary>
    /// Opens the menu with <paramref name="commands"/> at <paramref name="position"/>,
    /// taking the menu from wherever it was open.
    /// Each item shows its shortcut, and commands that ask for a number get a submenu.
    /// </summary>
    /// <param name="position">Where it opens.</param>
    /// <param name="context">What the commands act on.</param>
    /// <param name="commands">Its potential contents.</param>
    /// <param name="view">Where the commands run.</param>
    /// <param name="closed">Called when this menu closes without a command running.</param>
    private static void Show(
        Vector2I position,
        CommandContext context,
        IEnumerable<Command> commands,
        ICommandView view,
        Action closed
    )
    {
        var menu = Menu();
        menu.Hide();
        Done();

        menu.Clear(freeSubmenus: true);
        Items.Clear();
        _view = view;
        _closed = closed;
        Fill(menu, context, commands);

        Callable.From(() => Open(menu, position)).CallDeferred();
    }

    /// <summary>Whether the menu is showing.</summary>
    public static bool IsOpen => GodotObject.IsInstanceValid(_menu) && _menu.Visible;

    private static void Fill(PopupMenu menu, CommandContext context, IEnumerable<Command> commands)
    {
        foreach (var (command, targets) in CommandList.ForMenu(context, commands))
        {
            if (command == Command.Divider)
            {
                menu.AddSeparator();
                continue;
            }

            // Ids skip the separators, and count across submenus, so they index the items.
            int id = Items.Count;
            Items.Add((command, targets));
            menu.AddItem(command.Label(targets.Count), id);
            int index = menu.GetItemIndex(id);
            menu.SetItemDisabled(index, !command.IsAvailable());

            // A key shows only if pressing it here would run this command.
            bool keys = CommandList.RunsFromKeys(command, context);
            if (keys && command.ShortcutLabel() is { } shortcut)
                menu.SetItemShortcut(index, shortcut);

            if (command.Icon != null)
            {
                menu.SetItemIcon(index, GD.Load<Texture2D>(command.Icon));
                menu.SetItemIconMaxWidth(index, IconSize);
            }

            if (command is Submenu submenu)
            {
                var sub = NewMenu($"Submenu_{id}");
                Fill(sub, context, submenu.Items(ProjectService.Instance));
                menu.AddChild(sub);
                menu.SetItemSubmenuNode(index, sub);
            }

            if (command.AsksForNumber)
                AddNumberSubmenu(menu, command, targets, index, keys);
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
        menu.Visible = true;
        // Fits the menu to its items before placing it.
        menu.ResetSize();

        // Opens away from the edges it would overflow, and stays in the window.
        var size = menu.Size;
        var bounds = (Vector2I)menu.GetParent<Viewport>().GetVisibleRect().Size;
        if (position.X + size.X > bounds.X)
            position.X -= size.X;
        if (position.Y + size.Y > bounds.Y)
            position.Y -= size.Y;
        menu.Position = position.Clamp(Vector2I.Zero, (bounds - size).Max(Vector2I.Zero));
    }

    // Made on first use, in the main window.
    private static PopupMenu Menu()
    {
        if (GodotObject.IsInstanceValid(_menu))
            return _menu;

        _menu = NewMenu("CommandMenu");
        _menu.PopupHide += Done;
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(_menu);
        return _menu;
    }

    // The menu, or one of its submenus of commands, which run the command picked.
    private static PopupMenu NewMenu(string name)
    {
        var menu = new PopupMenu { Name = name };
        menu.IdPressed += id =>
        {
            var (command, targets) = Items[(int)id];
            // The item of a command that asks for a number only opens its submenu.
            if (!command.AsksForNumber)
                Run(command, targets, 1);
        };
        return menu;
    }

    // Tells whoever opened what the menu shows that it's done with.
    private static void Done()
    {
        var closed = _closed;
        _closed = null;
        closed?.Invoke();
    }

    // Done first, so a command can change what closing changes, like Duplicate entering spawn mode.
    private static void Run(Command command, IReadOnlyList<Target> targets, int number)
    {
        Done();
        command.Run(targets, number, _view);
    }

    private static void AddNumberSubmenu(
        PopupMenu menu,
        Command command,
        IReadOnlyList<Target> targets,
        int index,
        bool keys
    )
    {
        var sub = new PopupMenu { Name = $"NumberSubmenu_{index}" };
        var numbers = new List<int>();
        int last = Math.Min(command.MaxNumber(targets) ?? SubmenuNumbers, MaxSubmenuNumbers);
        for (int n = 1; n <= last; n++)
        {
            sub.AddItem(n.ToString());
            numbers.Add(n);
            // The number keys run it with that number, so they show as its shortcuts.
            if (keys && command.NumberLabel(n) is { } shortcut)
                sub.SetItemShortcut(sub.ItemCount - 1, shortcut);
        }
        if (command.InfiniteOption != null)
        {
            sub.AddItem(command.InfiniteOption);
            numbers.Add(int.MaxValue);
        }

        sub.IndexPressed += i => Run(command, targets, numbers[(int)i]);
        menu.AddChild(sub);
        menu.SetItemSubmenuNode(index, sub);
    }
}
