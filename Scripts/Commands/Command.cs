using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>
/// The stable name of a <see cref="Command"/>, like "component.flip".
/// </summary>
public readonly record struct CommandName(string Value);

/// <summary>
/// How many targets a <see cref="RecordCommand{T}"/> can act on.
/// When <see cref="One"/> is used, the Command won't appear when multiple targets are selected.
/// Useful for Commands like "Edit Prototype", where multiple targets would be ambiguous.
/// </summary>
public enum TargetCount
{
    /// <summary>Should not appear unless only one relevant target is in the context.</summary>
    One = 1,

    /// <summary>Should appear even when multiple relevant targets are in the context.</summary>
    Many = 2,
}

/// <summary>
/// The parts of a <see cref="CommandContext"/> a Command can target, set with <see cref="Command.ActsOn"/>.
/// </summary>
[Flags]
public enum Context
{
    /// <summary>
    /// The records the user targeted. Usually the selection, but can also be the hovered target.
    /// </summary>
    Selected = 1,

    /// <summary>
    /// Records that are contained within the selected targets, like a deck's cards or a DataSet's rows.
    /// </summary>
    Contents = 2,

    /// <summary>
    /// What the selected records are part of, like a card's deck.
    /// </summary>
    Containers = 4,

    /// <summary>
    /// What the selected targets refer to, like a component's prototype or data row.
    /// </summary>
    Referenced = 8,

    /// <summary>
    /// What record the whole view shows, like the dataset editor's currently open dataset.
    /// </summary>
    View = 16,
}

/// <summary>
/// What commands act on. Usually the selection or a hovered element.
/// </summary>
public sealed class CommandContext
{
    /// <summary>
    /// What the user is targeting. Usually the selection, but for keys it can be the hovered target.
    /// </summary>
    public IEnumerable<Target> Selected
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Targets that are contained within the selection, like a selected deck's cards.
    /// Matching commands act on these when their <see cref="Command.ActsOn"/> includes them.
    /// </summary>
    public IEnumerable<Target> Contents
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Targets that contain the selection, like a selected card's current deck.
    /// </summary>
    public IEnumerable<Target> Containers
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// What the selected targets refer to, like a component's prototype or data row.
    /// </summary>
    public IEnumerable<Target> Referenced
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// The record the view shows, from its <see cref="ICommandView.SelectionScope"/>, or empty.
    /// </summary>
    public IEnumerable<Target> View
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Custom commands for this view to add to the context menu, like the table's Zoom to Component.
    /// </summary>
    public IReadOnlyList<Command> Local { get; init; } = [];

    /// <summary>
    /// <para>The full context of what's targeted in <paramref name="view"/>, read through <paramref name="R"/>.</para>
    ///
    /// The <see cref="CommandContext"/> is derived from the player's selection records.
    /// If there is no selection and <paramref name="keys"/> is true, the hovered target from <see cref="ICommandView.Hovered"/> is treated like the selection.
    /// The <see cref="Containers"/>, <see cref="Contents"/>, and <see cref="Referenced"/> are all derived from the selected targets.
    /// </summary>
    public static CommandContext Of(ICommandView view, IRecordReader R, bool keys = false)
    {
        if (view == null)
            return new();

        bool scoped = view.SelectionScope.TryGet(R, out var scope);
        var selected = scoped
            ? R.GetSelection(scope)?.Targets ?? ImmutableHashSet<Target>.Empty
            : ImmutableHashSet<Target>.Empty;
        var chosen = selected.IsEmpty && keys ? view.Hovered().ToImmutableHashSet() : selected;

        IEnumerable<Target> Related(
            IEnumerable<Target> targets,
            Func<Target, IEnumerable<Target>> relation
        ) => targets.SelectMany(relation).Where(t => !chosen.Contains(t));

        // Each target is visited once, so if a relation ever does loop back it doesn't hang.
        HashSet<Target> Followed(Func<Target, IEnumerable<Target>> relation)
        {
            var found = new HashSet<Target>();
            for (
                var next = Related(chosen, relation).ToList();
                next.Count > 0;
                next = Related(next, relation).Where(t => !found.Contains(t)).ToList()
            )
                found.UnionWith(next);
            return found;
        }

        return new()
        {
            Selected = chosen,
            Contents = Followed(t => t.Contents(R)),
            Containers = Followed(t => t.Containers(R)),
            Referenced = Related(chosen, t => t.Referenced(R)),
            View = scoped && scope != SnowTag.Empty ? [new RecordTarget(scope)] : [],
            Local = view.Commands,
        };
    }
}

/// <summary>
/// A window or panel that commands run in.
/// </summary>
public interface ICommandView
{
    /// <summary>
    /// <para>The record that this view is operating on, like a dataset editor's currently open dataset.</para>
    ///
    /// <see cref="SnowTag.Empty"/> is used for the table.
    /// Otherwise, use the <see cref="SnowTag"/> of the record.
    /// Leave it unset in a view without a selection, or while there isn't a record at the moment.
    /// </summary>
    WatchableValue<SnowTag> SelectionScope { get; }

    /// <summary>
    /// The hovered target that keys act on when nothing is selected.
    /// Menus and buttons never use it.
    /// </summary>
    IEnumerable<Target> Hovered() => [];

    /// <summary>
    /// Extra custom commands for this view to add to its context menu.
    /// </summary>
    IReadOnlyList<Command> Commands => [];

    /// <summary>
    /// <para>Whether an undo or redo issued in this view should target <paramref name="effect"/>.</para>
    ///
    /// Returning true will make undo or redo in this view reverse or reapply this effect.
    /// Returning false will make them skip the effect and search further backwards for another candidate.
    /// </summary>
    bool UndoScope(Effect effect);
}

/// <summary>
/// Something the user runs from a context menu or a keyboard shortcut.
/// Every command is defined once, in a class for what it acts on, and listed in <see cref="CommandList"/>.
/// </summary>
public abstract class Command
{
    /// <summary>
    /// A dividing line between menu items.
    /// Command menus merge adjacent dividers and trim dividers left at either end.
    /// </summary>
    public static readonly Command Divider = new DividerCommand();

    /// <summary>
    /// The commands unique name, with dot separation and snake case "some.command_name"
    /// </summary>
    public CommandName Name { get; set; }

    /// <summary>
    /// The menu label, e.g. "Edit Prototype".
    /// For a command with targets, "{0}" is their number: "Delete {0}" shows "Delete 5".
    /// With a <see cref="Noun"/>, it's followed by what they are: "Delete 5 Components".
    /// </summary>
    public string Caption { get; init; }

    /// <summary>What one target is called, and several, after the count in the caption. Null for none.</summary>
    public (string One, string Many)? Noun { get; init; }

    /// <summary>The menu label for <paramref name="count"/> targets.</summary>
    public string Label(int count)
    {
        return Fits(0)
            ? Caption
            : string.Format(
                Caption,
                Noun is var (one, many) ? $"{count} {(count == 1 ? one : many)}" : count.ToString()
            );
    }

    /// <summary>
    /// The keys that run it, made with <see cref="Shortcuts.Key"/> or <see cref="Shortcuts.Ctrl"/>.
    /// The first one shows in menus.
    /// </summary>
    public IReadOnlyList<InputEventKey> Keys { get; init; } = [];

    private StringName _action;

    /// <summary>
    /// The <see cref="InputMap"/> action holding its <see cref="Keys"/>, named by its <see cref="Name"/>,
    /// so the keys can be changed while the game runs. Null when it has no keys.
    /// </summary>
    public StringName Action => Keys.Count == 0 ? null : _action ??= Name.Value;

    /// <summary>
    /// Set to false to avoid showing this command in the context menu.
    /// </summary>
    public bool ShowInMenu { get; init; } = true;

    /// <summary>Set to true to ask for a number through a submenu, such as how many cards to draw.</summary>
    public bool AsksForNumber { get; init; }

    /// <summary>
    /// When <see cref="AsksForNumber"/> = true, an extra choice after the numbers, such as "All",
    /// which runs the command with <see cref="int.MaxValue"/>. Null for none.
    /// </summary>
    public string InfiniteOption { get; init; }

    /// <summary>
    /// Set to true to have the number keys serve as keyboard shortcuts.
    /// </summary>
    public bool NumberKeys { get; init; }

    /// <summary>
    /// The parts of the context this Command will act on, in terms of the user's selection.
    /// By default, a Command only acts on directly selected records.
    /// <list type="number">
    /// <item><see cref="Context.Selected"/> acts on the selected records (the most direct)</item>
    /// <item><see cref="Context.Contents"/> acts on the records inside the selected records, like a selected bag's tokens</item>
    /// <item><see cref="Context.Containers"/> acts on the records containing the selected records, like a selected card's deck</item>
    /// <item><see cref="Context.Referenced"/> acts on the records referenced by the selected records, like a selected cube's prototype</item>
    /// <item><see cref="Context.View"/> acts on the record the whole view shows, like the dataset editor's open dataset</item>
    /// </list>
    /// You can act on multiple Contexts simultaneously with
    /// <code>ActsOn = Context.Selected | Context.Contents | Context.Containers | Context.Referenced</code>
    /// So a Delete Command with <c>Context.Selected | Context.Contents</c> will delete the selected records and their contents.
    /// Keyboard shortcuts skip <see cref="Context.Containers"/> and <see cref="Context.Referenced"/>,
    /// so a key never reaches past what's selected. Menus and CommandButtons use all configured parts.
    /// </summary>
    public Context ActsOn { get; init; } = Context.Selected;

    /// <summary>
    /// The path of an icon shown in the menu's left column, or null for none.
    /// Commands for one kind of component use the icon the component creation dialog shows for it.
    /// </summary>
    public string Icon { get; init; }

    /// <summary>
    /// Returns true if this command can act on <paramref name="target"/>:
    /// a record of the kind it's for, that it has behavior for.
    /// </summary>
    public abstract bool Applies(IRecordReader R, Target target);

    /// <summary>Whether it can act on <paramref name="count"/> targets that it applies to.</summary>
    public abstract bool Fits(int count);

    /// <summary>
    /// Whether the Command can run now.
    /// When false, it's disabled in menus and keyboard shortcuts won't trigger it.
    /// </summary>
    public virtual bool IsAvailable(IRecordReader R) => true;

    /// <summary>
    /// When <see cref="AsksForNumber"/> = true, the highest number worth offering for these targets,
    /// like a die's face count, or null for the menu's default.
    /// </summary>
    public virtual int? MaxNumber(IReadOnlyList<Target> targets) => null;

    /// <summary>
    /// Acts on the targets. <paramref name="number"/> is 1 unless the command asks for one,
    /// and <see cref="int.MaxValue"/> is its <see cref="InfiniteOption"/>.
    /// <paramref name="view"/> is where it runs, or null outside any view.
    /// </summary>
    public abstract void Run(IReadOnlyList<Target> targets, int number, ICommandView view);

    /// <summary>
    /// Returns true if <paramref name="e"/> runs this command, and the number it gives.
    /// Modifiers must match exactly.
    /// </summary>
    public bool Matches(InputEvent e, out int number)
    {
        number = 1;
        if (Action != null && Shortcuts.Pressed(e, Action))
            return true;
        if (!NumberKeys)
            return false;
        for (int n = 1; n <= Shortcuts.NumberCount; n++)
        {
            if (Shortcuts.Pressed(e, Shortcuts.Number(n)))
            {
                number = n;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The keyboard shortcut to show beside this command in a menu, or null.
    /// </summary>
    public Shortcut ShortcutLabel() => Shortcuts.Label(Action);

    /// <summary>
    /// The number key to show beside <paramref name="number"/> in the menu's number submenu,
    /// or null when the number keys don't run it.
    /// </summary>
    public Shortcut NumberLabel(int number) =>
        NumberKeys ? Shortcuts.Label(Shortcuts.Number(number)) : null;

    /// <summary>
    /// Submits the effects as one event made by this command.
    /// </summary>
    protected void Submit(IEnumerable<Effect> effects)
    {
        if (effects == null)
            return;
        EventSynchronizer.Instance?.Submit(TableEvent.Now(effects.ToArray(), Name));
    }
}

/// <summary>
/// A command on records of one type.
/// </summary>
public sealed class RecordCommand<T> : Command
    where T : class, IReplicated
{
    /// <summary>
    /// Determines whether this command can operate on a given <paramref name="record"/>.
    /// </summary>
    /// <param name="reader">The record reader to pull data.</param>
    /// <param name="record">The record that this command may apply to.</param>
    /// <returns>True if this command has behavior for the given <paramref name="record"/>.</returns>
    public delegate bool AppliesToDelegate(IRecordReader reader, T record);

    /// <summary>
    /// <para>Returns true if this command has behavior for the given record.</para>
    /// <para>For example, Shuffle only applies to decks.</para>
    /// </summary>
    public AppliesToDelegate AppliesTo { get; init; } = (_, _) => true;

    public TargetCount Count { get; init; } = TargetCount.Many;

    /// <summary>
    /// Produces the <see cref="Effect"/>s from this command.
    /// </summary>
    /// <param name="reader">The record reader to pull data.</param>
    /// <param name="records">The records that were targeted with this command.</param>
    /// <param name="number">When <see cref="Command.AsksForNumber"/> is true, the number provided by the user.</param>
    /// <returns>The effects, submitted as one event.</returns>
    public delegate IEnumerable<Effect> EffectsDelegate(
        IRecordReader reader,
        IReadOnlyList<T> records,
        int number
    );

    /// <summary>
    /// Generates the effects from this command on the given records, submitted as one event.
    /// Returning an empty list still fires an event.
    /// returning null will not fire an event.
    /// The third parameter provides a number if <see cref="Command.AsksForNumber"/> is true, like with setting a die face.
    /// </summary>
    public EffectsDelegate Effects { get; init; }

    /// <summary>
    /// An action to apply arbitrary side-effects.
    /// The second parameter provides a number if <see cref="Command.AsksForNumber"/> is true.
    /// </summary>
    public Action<IReadOnlyList<T>, int> SideEffects { get; init; }

    /// <summary>
    /// When <see cref="Command.AsksForNumber"/> is true, return the highest number worth offering for the records.
    /// </summary>
    public Func<IRecordReader, IReadOnlyList<T>, int> NumberLimit { get; init; }

    public override bool Applies(IRecordReader R, Target target) =>
        Record(R, target) is { } r && AppliesTo(R, r);

    public override bool Fits(int count) => Count == TargetCount.One ? count == 1 : count > 0;

    public override int? MaxNumber(IReadOnlyList<Target> targets) =>
        NumberLimit?.Invoke(Reader, Records(Reader, targets));

    public override void Run(IReadOnlyList<Target> targets, int number, ICommandView view)
    {
        var records = Records(Reader, targets);
        SideEffects?.Invoke(records, number);
        if (Effects != null && records.Count > 0)
            Submit(Effects(Reader, records, number));
    }

    private static IRecordReader Reader => RecordService.Instance;

    private static T Record(IRecordReader R, Target target) =>
        target is RecordTarget r ? R?.Get<T>(r.Id) : null;

    // A record may be gone since the menu opened.
    private static List<T> Records(IRecordReader R, IReadOnlyList<Target> targets) =>
        targets.Select(t => Record(R, t)).Where(r => r != null).ToList();
}

/// <summary>
/// A command on targets that are part of a record.
/// </summary>
public sealed class TargetCommand<T> : Command
    where T : Target
{
    /// <summary>
    /// Returns true if this command has behavior for the target.
    /// A target can outlive what it points at, so this also checks that it's still there.
    /// </summary>
    public Func<IRecordReader, T, bool> AppliesTo { get; init; } = (_, _) => true;

    public TargetCount Count { get; init; } = TargetCount.Many;

    /// <summary>
    /// Generate the effects from this command on the given targets, submitted as one event.
    /// returning null will not fire an event.
    /// </summary>
    public Func<IRecordReader, IReadOnlyList<T>, int, IEnumerable<Effect>> Effects { get; init; }

    /// <summary>
    /// Run so that this command can perform actions other than change records, such as starting a rename.
    /// </summary>
    public Action<IReadOnlyList<T>, int> SideEffects { get; init; }

    public override bool Applies(IRecordReader R, Target target) =>
        target is T t && AppliesTo(R, t);

    public override bool Fits(int count) => Count == TargetCount.One ? count == 1 : count > 0;

    public override void Run(IReadOnlyList<Target> targets, int number, ICommandView view)
    {
        var typed = targets.OfType<T>().Where(t => AppliesTo(Reader, t)).ToList();
        SideEffects?.Invoke(typed, number);
        if (Effects != null && typed.Count > 0)
            Submit(Effects(Reader, typed, number));
    }

    private static IRecordReader Reader => RecordService.Instance;
}

/// <summary>A command that does not act on selected targets, such as undo.</summary>
public sealed class GlobalCommand : Command
{
    public override bool Applies(IRecordReader R, Target target) => false;

    public override bool Fits(int count) => count == 0;

    /// <summary>
    /// Whether the command can run right now.
    /// </summary>
    public Func<IRecordReader, bool> Enabled { get; init; }

    /// <summary>
    /// Generates the effects, submitted as one event.
    /// Returning an empty list still fires an event
    /// Returning null does not fire an event.
    /// </summary>
    public Func<IRecordReader, IEnumerable<Effect>> Effects { get; init; }

    /// <summary>What it does, given the view it runs in, or null outside any view.</summary>
    public Action<ICommandView> SideEffects { get; init; }

    public override bool IsAvailable(IRecordReader R) => Enabled?.Invoke(R) ?? true;

    public override void Run(IReadOnlyList<Target> targets, int number, ICommandView view)
    {
        SideEffects?.Invoke(view);
        if (Effects != null)
            Submit(Effects(RecordService.Instance));
    }
}

/// <summary>
/// A menu item with a submenu of other commands.
/// It can go in any list of commands a menu shows, including another submenu,
/// and shows only when one of its commands would show.
/// </summary>
public sealed class Submenu : Command
{
    /// <summary>
    /// The commands it offers, built each time a menu shows this submenu.
    /// They act like any other command in the menu.
    /// Items without their own <see cref="Command.Name"/> inherit the submenu's.
    /// </summary>
    public Func<IRecordReader, IEnumerable<Command>> Items { get; init; }

    public IEnumerable<Command> ItemsFor(IRecordReader R) =>
        Items(R)
            .Select(item =>
            {
                if (item.Name.Value == null && item != Divider)
                    item.Name = Name;
                return item;
            });

    public override bool Applies(IRecordReader R, Target target) => false;

    public override bool Fits(int count) => count == 0;

    // Picking it opens its menu.
    public override void Run(IReadOnlyList<Target> targets, int number, ICommandView view) { }
}

// This is just a sentinel so that we can put dividers in command lists.
internal sealed class DividerCommand : Command
{
    public override bool Applies(IRecordReader R, Target target) => false;

    public override bool Fits(int count) => count == 0;

    public override void Run(IReadOnlyList<Target> targets, int number, ICommandView view) { }
}

/// <summary>
/// Utilities to attach <see cref="ICommandView"/> to a <see cref="Node"/>.
/// </summary>
public static class CommandViews
{
    private static readonly Dictionary<Node, ICommandView> Attached = new();

    /// <summary>
    /// Makes <paramref name="view"/> take the <see cref="Command">s
    /// for everything under <paramref name="node"/> until either leaves the tree.
    /// </summary>
    public static void Attach(Node node, ICommandView view)
    {
        Attached[node] = view;
        DetachOnExit(node, node, view);
        if (view is Node owner && owner != node)
            DetachOnExit(owner, node, view);
    }

    private static void DetachOnExit(Node leaving, Node node, ICommandView view) =>
        leaving.Connect(
            Node.SignalName.TreeExiting,
            Callable.From(() =>
            {
                // Don't remove it if it was Attached to a new view.
                if (Attached.GetValueOrDefault(node) == view)
                    Attached.Remove(node);
            }),
            (uint)GodotObject.ConnectFlags.OneShot // our callback is only called once
        );

    /// <summary>
    /// Makes everything under <paramref name="node"/> a view with nothing to act on,
    /// where undo walks through <paramref name="undoScope"/>, until it leaves the tree.
    /// </summary>
    public static void Attach(Node node, Func<Effect, bool> undoScope) =>
        Attach(node, new ScopeView(undoScope));

    private sealed class ScopeView(Func<Effect, bool> undoScope) : ICommandView
    {
        public WatchableValue<SnowTag> SelectionScope { get; } = new();

        public bool UndoScope(Effect fx) => undoScope(fx);
    }

    /// <summary>The view for the viewport's focused control, or null if it has none.</summary>
    public static ICommandView Find(Viewport viewport) =>
        Of(viewport.GuiGetFocusOwner() ?? (Node)viewport);

    /// <summary>
    /// The view that <paramref name="node"/> is in.
    /// </summary>
    public static ICommandView Of(Node node)
    {
        var viewport = node.GetViewport();
        for (Node n = node; n != null; n = n.GetParent())
        {
            if (n is ICommandView view)
                return view;
            if (Attached.TryGetValue(n, out var attached))
                return attached;
            // A window only takes its own views, not the ones of the windows it's inside.
            if (n == viewport)
                break;
        }
        return null;
    }
}
