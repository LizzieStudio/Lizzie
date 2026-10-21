using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// The stable name of a <see cref="Command"/>, like "component.flip".
/// </summary>
public readonly record struct CommandName(string Value);

/// <summary>
/// How many targets a <see cref="AtomCommand{T}"/> can act on.
/// </summary>
public enum TargetCount
{
    /// <summary>
    /// Should only appear if exactly one relevant target is in the context.
    /// </summary>
    One = 1,

    /// <summary>
    /// Should appear if any relevant targets are in the context.
    /// </summary>
    Many = 2,

    /// <summary>
    /// Should appear if 0 or 1 relevant targets are in the context.
    /// </summary>
    Optional = 3,
}

/// <summary>
/// The parts of a <see cref="CommandContext"/> a Command can target, set with <see cref="Command.ActsOn"/>.
/// </summary>
[Flags]
public enum Context
{
    /// <summary>
    /// The atoms the user targeted. Usually the selection, but can also be the hovered target.
    /// </summary>
    Selected = 1,

    /// <summary>
    /// Atoms that are contained within the selected targets, like a deck's cards or a DataSet's rows.
    /// </summary>
    Contents = 2,

    /// <summary>
    /// What the selected atoms are part of, like a card's deck.
    /// </summary>
    Containers = 4,

    /// <summary>
    /// What the selected targets refer to, like a component's prototype or data row.
    /// </summary>
    Referenced = 8,

    /// <summary>
    /// What atom the whole view shows, like the dataset editor's currently open dataset.
    /// </summary>
    View = 16,
}

/// <summary>
/// A window or panel that commands run in.
/// </summary>
public interface ICommandView
{
    /// <summary>
    /// Where selections in this view live, like the table showing a stage.
    ///
    /// <para>
    /// A view without a selection gives null.
    /// A Watch that reads it through <paramref name="R"/> runs again when it changes.
    /// </para>
    /// </summary>
    SelectionPlace? SelectionScope(IAtomReader R) => null;

    /// <summary>
    /// The atom the whole view shows, like the dataset editor's open dataset, or null for none.
    /// Commands that act on <see cref="Context.View"/> act on it.
    /// </summary>
    AtomRef? Shown(IAtomReader R) => null;

    /// <summary>
    /// The hovered target that keys act on when nothing is selected.
    /// Menus and buttons never use it.
    /// </summary>
    IEnumerable<AtomRef> Hovered() => [];

    /// <summary>
    /// Extra custom commands for this view to add to its context menu.
    /// </summary>
    IReadOnlyList<Command> Commands => [];

    /// <summary>
    /// <para>The undo group of a gesture the local player has open in this view, or <see cref="SnowportId.Empty"/>.</para>
    ///
    /// <para>
    /// Commands that run on targets join it, so the gesture undoes as one.
    /// Commands without targets never join.
    /// </para>
    /// </summary>
    SnowportId Gesture => SnowportId.Empty;

    /// <summary>
    /// <para>Whether an undo or redo issued in this view should target an event that writes <paramref name="atom"/>.</para>
    ///
    /// Returning true will make undo or redo in this view reverse or reapply the write.
    /// Returning false will make them skip it and search further backwards for another candidate.
    /// </summary>
    bool UndoScope(Atom atom);
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
    /// The menu label, e.g. "Edit Component".
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
    /// By default, a Command only acts on directly selected atoms.
    /// <list type="number">
    /// <item><see cref="Context.Selected"/> acts on the selected atoms (the most direct)</item>
    /// <item><see cref="Context.Contents"/> acts on the atoms inside the selected atoms, like a selected bag's tokens</item>
    /// <item><see cref="Context.Containers"/> acts on the atoms containing the selected atoms, like a selected card's deck</item>
    /// <item><see cref="Context.Referenced"/> acts on the atoms referenced by the selected atoms, like a selected cube's prototype</item>
    /// <item><see cref="Context.View"/> acts on the atom the whole view shows, like the dataset editor's open dataset</item>
    /// </list>
    /// You can act on multiple Contexts simultaneously with
    /// <code>ActsOn = Context.Selected | Context.Contents | Context.Containers | Context.Referenced</code>
    /// So a Delete Command with <c>Context.Selected | Context.Contents</c> will delete the selected atoms and their contents.
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
    /// The tooltip that shows when the user hovers over the command.
    /// </summary>
    public string Tooltip { get; init; }

    /// <summary>
    /// Its <see cref="Tooltip"/> with <paramref name="reason"/> below.
    /// </summary>
    public string TooltipWith(string reason) =>
        string.Join('\n', new[] { Tooltip, reason }.Where(s => s != null));

    /// <summary>
    /// Returns true if this command can act on <paramref name="target"/>:
    /// an atom of the kind it's for, that it has behavior for.
    /// </summary>
    public abstract bool Applies(IAtomReader R, AtomRef target);

    /// <summary>
    /// Why it can't act on <paramref name="target"/>, or null when it can.
    /// </summary>
    public virtual string Blocked(IAtomReader R, AtomRef target) => null;

    /// <summary>Whether it can act on <paramref name="count"/> targets that it applies to.</summary>
    public abstract bool Fits(int count);

    /// <summary>
    /// Why it's disabled with <paramref name="count"/> targets, or null for no reason.
    /// </summary>
    public virtual string DisabledReason(IAtomReader R, int count, IReadOnlyList<string> blocked) =>
        null;

    /// <summary>
    /// Whether the Command can run now.
    /// When false, it's disabled in menus and keyboard shortcuts won't trigger it.
    /// </summary>
    public virtual bool IsAvailable(IAtomReader R) => true;

    /// <summary>
    /// When <see cref="AsksForNumber"/> = true, the highest number worth offering for these targets,
    /// like a die's face count, or null for the menu's default.
    /// </summary>
    public virtual int? MaxNumber(IReadOnlyList<AtomRef> targets) => null;

    /// <summary>
    /// Acts on the targets. <paramref name="number"/> is 1 unless the command asks for one,
    /// and <see cref="int.MaxValue"/> is its <see cref="InfiniteOption"/>.
    /// <paramref name="view"/> is where it runs, or null outside any view.
    /// </summary>
    public abstract void Run(IReadOnlyList<AtomRef> targets, int number, ICommandView view);

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
        for (int n = 1; n <= ShortcutActions.NumberCount; n++)
        {
            if (Shortcuts.Pressed(e, ShortcutActions.Number(n)))
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
    public Shortcut ShortcutLabel() => ShortcutActions.Label(Action);

    /// <summary>
    /// The number key to show beside <paramref name="number"/> in the menu's number submenu,
    /// or null when the number keys don't run it.
    /// </summary>
    public Shortcut NumberLabel(int number) =>
        NumberKeys ? ShortcutActions.Label(ShortcutActions.Number(number)) : null;

    /// <summary>
    /// <para>Writes the changes as one event made by this command.</para>
    ///
    /// <para>
    /// When <paramref name="view"/> has a <see cref="ICommandView.Gesture"/> open, the event joins it.
    /// Pass a null view for a command without targets, which never joins one.
    /// </para>
    /// </summary>
    protected void Submit(IEnumerable<IChange> changes, ICommandView view)
    {
        if (changes == null)
            return;
        var gesture = view?.Gesture ?? SnowportId.Empty;
        if (gesture == SnowportId.Empty)
            AtomService.Instance?.Write(Name, changes);
        else
            AtomService.Instance?.Append(gesture, Name, changes);
    }
}

/// <summary>
/// A command on atoms of type <typeparamref name="T"/>.
/// It acts on what <see cref="AtomReaderExtensions.Resolve"/> gives for each target,
/// so it also reaches an atom that was never written, like an empty cell.
/// </summary>
/// <remarks>
/// To act on all atom types, set <typeparamref name="T"/> to <see cref="Atom"/>.
/// </remarks>
public sealed class AtomCommand<T> : Command
    where T : Atom
{
    /// <summary>
    /// Determines whether this command can operate on a given <paramref name="atom"/>.
    /// </summary>
    /// <param name="reader">The atom reader to pull data.</param>
    /// <param name="atom">The atom that this command may apply to.</param>
    /// <returns>True if this command has behavior for the given <paramref name="atom"/>.</returns>
    public delegate bool AppliesToDelegate(IAtomReader reader, T atom);

    /// <summary>
    /// <para>Returns true if this command has behavior for the given atom.</para>
    /// <para>For example, Shuffle only applies to decks.</para>
    /// </summary>
    public AppliesToDelegate AppliesTo { get; init; } = (_, _) => true;

    /// <summary>
    /// Define rules a relevant atom must also meet.
    /// Broken rules are displayed in the tooltip when the command is disabled.
    /// </summary>
    public IReadOnlyList<(string Reason, AppliesToDelegate Holds)> Requires { get; init; } = [];

    /// <inheritdoc cref="TargetCount" />
    public TargetCount Count { get; init; } = TargetCount.Many;

    /// <summary>
    /// Produces the changes this command makes, which will be passed to <see cref="AtomService.Write"/>
    /// Returning an empty list still fires an event, but returning null will not.
    /// </summary>
    /// <param name="reader">The atom reader to pull data.</param>
    /// <param name="atoms">The atoms that were targeted with this command.</param>
    /// <param name="number">When <see cref="Command.AsksForNumber"/> is true, the number provided by the user.</param>
    /// <returns>The changes, written as one event.</returns>
    public delegate IEnumerable<IChange> EffectsDelegate(
        IAtomReader reader,
        IReadOnlyList<T> atoms,
        int number
    );

    /// <inheritdoc cref="EffectsDelegate"/>
    public EffectsDelegate Effects { get; init; }

    /// <summary>
    /// An action to apply arbitrary side-effects.
    /// The second parameter provides a number if <see cref="Command.AsksForNumber"/> is true.
    /// </summary>
    public Action<IReadOnlyList<T>, int> SideEffects { get; init; }

    /// <summary>
    /// When <see cref="Command.AsksForNumber"/> is true, return the highest number worth offering for the atoms.
    /// </summary>
    public Func<IAtomReader, IReadOnlyList<T>, int> NumberLimit { get; init; }

    public override bool Applies(IAtomReader R, AtomRef target) => Atom(R, target) != null;

    public override string Blocked(IAtomReader R, AtomRef target) =>
        R?.Resolve(target) is T atom && AppliesTo(R, atom)
            ? Requires.FirstOrDefault(rule => !rule.Holds(R, atom)).Reason
            : null;

    public override bool Fits(int count) =>
        Count switch
        {
            TargetCount.One => count == 1,
            TargetCount.Optional => count <= 1,
            _ => count > 0,
        };

    public override string DisabledReason(IAtomReader R, int count, IReadOnlyList<string> blocked)
    {
        if (Fits(count))
            return null;
        if (count == 0 && blocked.Count > 0)
            return string.Join("\n", blocked);

        var one = Noun?.One.ToLowerInvariant() ?? "item";
        return Count switch
        {
            TargetCount.One => $"Select one {one}.",
            TargetCount.Optional => $"Select one {one} at most.",
            _ => $"Select at least one {one}.",
        };
    }

    public override int? MaxNumber(IReadOnlyList<AtomRef> targets) =>
        NumberLimit?.Invoke(Reader, Atoms(Reader, targets));

    public override void Run(IReadOnlyList<AtomRef> targets, int number, ICommandView view)
    {
        var atoms = Atoms(Reader, targets);
        SideEffects?.Invoke(atoms, number);
        if (Effects != null && (atoms.Count > 0 || targets.Count == 0 && Fits(0)))
            Submit(Effects(Reader, atoms, number), view);
    }

    private static IAtomReader Reader => AtomService.Instance;

    // The atom it acts on, or what stands in for one never written, or null if it doesn't apply.
    private T Atom(IAtomReader R, AtomRef target) =>
        R?.Resolve(target) is T atom
        && AppliesTo(R, atom)
        && Requires.All(rule => rule.Holds(R, atom))
            ? atom
            : null;

    // An atom may be gone since the menu opened.
    private List<T> Atoms(IAtomReader R, IReadOnlyList<AtomRef> targets) =>
        targets.Select(t => Atom(R, t)).Where(r => r != null).ToList();
}

/// <summary>A command that does not act on selected targets, such as undo.</summary>
public sealed class GlobalCommand : Command
{
    public override bool Applies(IAtomReader R, AtomRef target) => false;

    public override bool Fits(int count) => count == 0;

    /// <summary>
    /// Whether the command can run right now.
    /// </summary>
    public Func<IAtomReader, bool> Enabled { get; init; }

    /// <summary>
    /// Define rules that must be met,
    /// Broken rules are displayed in the tooltip when the command is disabled.
    /// </summary>
    public IReadOnlyList<(string Reason, Func<IAtomReader, bool> Holds)> Requires { get; init; } =
    [];

    /// <summary>
    /// Generates the changes to write, as one event.
    /// Returning an empty list still fires an event
    /// Returning null does not fire an event.
    /// </summary>
    public Func<IAtomReader, IEnumerable<IChange>> Effects { get; init; }

    /// <summary>What it does, given the view it runs in, or null outside any view.</summary>
    public Action<ICommandView> SideEffects { get; init; }

    public override bool IsAvailable(IAtomReader R) =>
        (Enabled?.Invoke(R) ?? true) && Requires.All(rule => rule.Holds(R));

    public override string DisabledReason(
        IAtomReader R,
        int count,
        IReadOnlyList<string> blocked
    ) => Requires.FirstOrDefault(rule => !rule.Holds(R)).Reason;

    public override void Run(IReadOnlyList<AtomRef> targets, int number, ICommandView view)
    {
        SideEffects?.Invoke(view);
        if (Effects != null)
            Submit(Effects(AtomService.Instance), null);
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
    public Func<IAtomReader, IEnumerable<Command>> Items { get; init; }

    public IEnumerable<Command> ItemsFor(IAtomReader R) =>
        Items(R)
            .Select(item =>
            {
                if (item.Name.Value == null && item != Divider)
                    item.Name = Name;
                return item;
            });

    public override bool Applies(IAtomReader R, AtomRef target) => false;

    public override bool Fits(int count) => count == 0;

    // Picking it opens its menu.
    public override void Run(IReadOnlyList<AtomRef> targets, int number, ICommandView view) { }
}

// This is just a sentinel so that we can put dividers in command lists.
file sealed class DividerCommand : Command
{
    public override bool Applies(IAtomReader R, AtomRef target) => false;

    public override bool Fits(int count) => count == 0;

    public override void Run(IReadOnlyList<AtomRef> targets, int number, ICommandView view) { }
}

/// <summary>
/// Utilities to attach <see cref="ICommandView"/> to a <see cref="Node"/>.
/// </summary>
public static class CommandViews
{
    private static readonly Dictionary<Node, ICommandView> Attached = new();

    /// <summary>
    /// Makes <paramref name="view"/> take the <see cref="Command"/>s
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

    /// <summary>The view for the viewport's focused control, or null if it has none.</summary>
    public static ICommandView Find(Viewport viewport) =>
        Of(viewport.GuiGetFocusOwner() ?? (Node)viewport);

    /// <summary>
    /// The focused embedded window, or <paramref name="root"/>.
    /// </summary>
    public static Window FocusedWindow(Window root) =>
        root.GetEmbeddedSubwindows()
            .FirstOrDefault(w => w.Visible && w.HasFocus() && w is not Popup)
        ?? root;

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
