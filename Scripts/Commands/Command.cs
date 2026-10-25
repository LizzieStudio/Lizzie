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
/// Names the <see cref="Command"/> in a static field,
/// which is then made available to <see cref="CommandButton"/> and <see cref="CommandMenuItem"/>.
///
/// <code>
/// [Command("some.name")]
/// public static Command SomeName = new GlobalCommand
/// {
///     // ...
/// }
/// </code>
/// </summary>
/// <param name="name">Dot separated and snake case, like "component.flip".</param>
[AttributeUsage(AttributeTargets.Field)]
public sealed class CommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
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
    /// Atoms that are contained within the selected targets, like a deck's cards or a Dataset's rows.
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
    /// The atom the whole view is open on, like the dataset editor's open dataset.
    /// </summary>
    Subject = 16,
}

/// <summary>
/// A window or panel that commands run in, named by its class name.
/// </summary>
public interface ICommandView
{
    /// <summary>
    /// Where in this view the player is, like the view showing a stage. Selections in the view live there.
    ///
    /// <para>
    /// By default, the view as a whole. A view that shows one atom at a time is open on that atom,
    /// its <see cref="ViewPlace.Subject"/>, or gives null while it shows none.
    /// Commands that act on <see cref="Context.Subject"/> act on the subject.
    /// A Watch that reads it through <paramref name="R"/> runs again when it changes.
    /// </para>
    /// </summary>
    ViewPlace? Place(IAtomReader R) => ViewPlace.Of(this);

    /// <summary>
    /// Checks whether <paramref name="target"/> can be selected in this view, so a selection of it is still valid.
    /// </summary>
    /// <remarks>
    /// Deleted atoms can keep their selection, so this call is used to check what's still on-screen.
    /// </remarks>
    bool CanSelect(IAtomReader R, AtomRef target) => R.Get(target) != null;

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
    /// </para>
    /// </summary>
    SnowportId Gesture => SnowportId.Empty;
}

/// <summary>
/// A place in a view, like the dataset editor showing one dataset.
///
/// <para>
/// Each view keeps its own selection in each place.
/// Otherwise Undo/Redo would change your selection in closed windows.
/// </para>
/// </summary>
/// <param name="View">The view's class name.</param>
/// <param name="Subject">The atom the view is open on, or null when it shows a whole list.</param>
public readonly record struct ViewPlace(string View, AtomRef? Subject)
{
    /// <summary>
    /// The place in view <typeparamref name="TView"/> open on <paramref name="subject"/>.
    /// </summary>
    public static ViewPlace Of<TView>(AtomRef? subject = null)
        where TView : ICommandView => new(typeof(TView).Name, subject);

    /// <summary>
    /// The place in <paramref name="view"/> open on <paramref name="subject"/>.
    /// </summary>
    public static ViewPlace Of(ICommandView view, AtomRef? subject = null) =>
        new(view.GetType().Name, subject);
}

/// <summary>
/// Something the user runs from a context menu or a keyboard shortcut.
/// Every command is defined once, in a static field of a class for what it acts on,
/// named by its <see cref="CommandAttribute"/>.
/// The public ones are in <see cref="CommandList.All"/>, and a view's own commands are private.
/// </summary>
public abstract class Command
{
    /// <summary>
    /// A dividing line between menu items.
    /// Command menus merge adjacent dividers and trim dividers left at either end.
    /// </summary>
    public static readonly Command Divider = new DividerCommand();

    /// <summary>
    /// The command's unique name, from the <see cref="CommandAttribute"/> on its field.
    /// </summary>
    public CommandName Name => CommandList.NameOf(this);

    /// <summary>
    /// The kind of atom it acts on, or null for a command without targets.
    /// Menus group commands by it, and keys may be shared across kinds.
    /// </summary>
    internal virtual Type TargetType => null;

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
    /// The parts of the context this Command will act on, in terms of the user's selection.
    /// By default, a Command only acts on directly selected atoms.
    /// <list type="number">
    /// <item><see cref="Context.Selected"/> acts on the selected atoms (the most direct)</item>
    /// <item><see cref="Context.Contents"/> acts on the atoms inside the selected atoms, like a selected bag's tokens</item>
    /// <item><see cref="Context.Containers"/> acts on the atoms containing the selected atoms, like a selected card's deck</item>
    /// <item><see cref="Context.Referenced"/> acts on the atoms referenced by the selected atoms, like a selected cube's prototype</item>
    /// <item><see cref="Context.Subject"/> acts on the atom the whole view is open on, like the dataset editor's open dataset</item>
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
    /// an atom of the kind it's for, that it has behavior for and that meets its requirements.
    /// </summary>
    public abstract bool CanActOn(IAtomReader R, AtomRef target);

    /// <summary>
    /// Why it can't act on <paramref name="target"/>, or null when it can.
    /// </summary>
    public virtual string Blocked(IAtomReader R, AtomRef target) => null;

    /// <summary>
    /// Whether it can act on <paramref name="count"/> targets that pass <see cref="CanActOn"/>.
    /// </summary>
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
    /// Acts on the targets, or does nothing when it can't act on them or isn't available.
    /// </summary>
    /// <param name="view">Where it runs, or null outside any view.</param>
    public abstract void Run(IReadOnlyList<AtomRef> targets, ICommandView view);

    /// <summary>
    /// Writes the changes as one event made by this command, or nothing when they're null.
    /// When <paramref name="view"/> has a <see cref="ICommandView.Gesture"/> open, the event joins it.
    /// </summary>
    protected void Submit(IEnumerable<IChange> changes, ICommandView view)
    {
        if (changes == null)
            return;
        var gesture = view?.Gesture ?? SnowportId.Empty;
        if (gesture == SnowportId.Empty)
            Atoms.R?.Write((Node)view, Name, changes);
        else
            Atoms.R?.Append(gesture, Name, changes);
    }

    /// <summary>
    /// Returns true if <paramref name="e"/> runs this command. Modifiers must match exactly.
    /// </summary>
    public virtual bool Matches(InputEvent e) => Action != null && Shortcuts.Pressed(e, Action);

    /// <summary>
    /// The keyboard shortcut to show beside this command in a menu, or null.
    /// </summary>
    public Shortcut ShortcutLabel() => ShortcutActions.Label(Action);
}

/// <summary>
/// A command on atoms of type <typeparamref name="T"/>, with the rules for which atoms it acts on.
/// It acts on what <see cref="AtomReaderExtensions.Resolve"/> gives for each target,
/// so it also reaches an atom that was never written, like an empty cell.
/// </summary>
/// <remarks>
/// To act on all atom types, set <typeparamref name="T"/> to <see cref="Atom"/>.
/// </remarks>
public abstract class TargetedCommand<T> : Command
    where T : Atom
{
    /// <summary>
    /// Determines whether this command can operate on a given <paramref name="atom"/>.
    /// </summary>
    /// <param name="R">The atom reader to pull data.</param>
    /// <param name="atom">The atom that this command may apply to.</param>
    /// <returns>True if this command has behavior for the given <paramref name="atom"/>.</returns>
    public delegate bool AppliesToDelegate(IAtomReader R, T atom);

    /// <summary>
    /// Returns true if this command has behavior for the given atom.
    /// When it's false for every target, the command doesn't show.
    /// </summary>
    public AppliesToDelegate AppliesTo { get; init; } = (_, _) => true;

    /// <summary>
    /// Define rules a relevant atom must also meet.
    /// Failing targets are left out. When too few are left, the command shows disabled with the reason.
    /// </summary>
    public IReadOnlyList<(string Reason, AppliesToDelegate Holds)> Requires { get; init; } = [];

    /// <summary>
    /// The fewest targets it acts on.
    /// </summary>
    public int MinTargets { get; init; } = 1;

    /// <summary>
    /// The most targets it acts on.
    /// </summary>
    public int MaxTargets { get; init; } = int.MaxValue;

    internal override Type TargetType => typeof(T);

    public override bool CanActOn(IAtomReader R, AtomRef target) => Atom(R, target) != null;

    public override string Blocked(IAtomReader R, AtomRef target) =>
        R?.Resolve(target) is T atom && AppliesTo(R, atom)
            ? Requires.FirstOrDefault(rule => !rule.Holds(R, atom)).Reason
            : null;

    public override bool Fits(int count) => MinTargets <= count && count <= MaxTargets;

    public override string DisabledReason(IAtomReader R, int count, IReadOnlyList<string> blocked)
    {
        if (Fits(count))
            return null;
        if (count < MinTargets && blocked.Count > 0)
            return string.Join("\n", blocked);

        var one = Noun?.One.ToLowerInvariant() ?? "item";
        var many = Noun?.Many.ToLowerInvariant() ?? "items";
        string Amount(int n) => n == 1 ? $"one {one}" : $"{n} {many}";
        return (MinTargets, MaxTargets) switch
        {
            var (min, max) when min == max => $"Select {Amount(min)}.",
            (var min, int.MaxValue) => $"Select at least {Amount(min)}.",
            (0, var max) => $"Select {Amount(max)} at most.",
            var (min, max) => $"Select {min} to {max} {many}.",
        };
    }

    // The atom it acts on, or what stands in for one never written, or null if it doesn't apply.
    private T Atom(IAtomReader R, AtomRef target) =>
        R?.Resolve(target) is T atom
        && AppliesTo(R, atom)
        && Requires.All(rule => rule.Holds(R, atom))
            ? atom
            : null;

    /// <summary>
    /// The atoms it acts on, leaving out any gone since the menu opened,
    /// or null when too few are left.
    /// </summary>
    protected List<T> Resolved(IReadOnlyList<AtomRef> targets)
    {
        var atoms = targets.Select(t => Atom(Atoms.R, t)).Where(a => a != null).ToList();
        return Fits(atoms.Count) ? atoms : null;
    }
}

/// <summary>
/// A command on atoms of type <typeparamref name="T"/>.
/// </summary>
public sealed class AtomCommand<T> : TargetedCommand<T>
    where T : Atom
{
    /// <summary>
    /// Run whenever the user activates the command.
    /// </summary>
    /// <param name="R">The atom reader to pull data.</param>
    /// <param name="atoms">The atoms that were targeted with this command.</param>
    /// <returns>
    /// The atom writes, written as one event.
    /// Null will not fire an event, but an empty collection will.
    /// </returns>
    public delegate IEnumerable<IChange> EffectsDelegate(IAtomReader R, IReadOnlyList<T> atoms);

    /// <inheritdoc cref="EffectsDelegate"/>
    public required EffectsDelegate Effects { get; init; }

    public override void Run(IReadOnlyList<AtomRef> targets, ICommandView view)
    {
        if (Resolved(targets) is { } atoms)
            Submit(Effects(Atoms.R, atoms), view);
    }
}

/// <summary>
/// A command that offers a submenu of choices.
/// </summary>
public interface IChoiceCommand
{
    /// <summary>
    /// Whether number key n picks the nth choice.
    /// </summary>
    bool NumberKeys { get; }

    /// <summary>
    /// What it offers for <paramref name="targets"/>, in order.
    /// </summary>
    IReadOnlyList<(string Label, int Value)> ChoicesFor(IReadOnlyList<AtomRef> targets);

    /// <summary>
    /// The number key <paramref name="e"/> presses, from 1, when the number keys pick choices.
    /// </summary>
    int? NumberPressed(InputEvent e);

    /// <summary>
    /// Acts on the targets with the picked choice's <paramref name="value"/>.
    /// </summary>
    void Choose(IReadOnlyList<AtomRef> targets, int value, ICommandView view);
}

/// <summary>
/// A command on atoms of type <typeparamref name="T"/> that offers a submenu of choices,
/// and writes the <see cref="Effects"/> of the one picked.
/// </summary>
public sealed class ChoiceCommand<T> : TargetedCommand<T>, IChoiceCommand
    where T : Atom
{
    /// <summary>
    /// What options it offers for the atoms.
    /// </summary>
    /// <param name="R">The atom reader to pull data.</param>
    /// <param name="atoms">The atoms that were targeted with this command.</param>
    public delegate IReadOnlyList<(string Label, int Value)> ChoicesDelegate(
        IAtomReader R,
        IReadOnlyList<T> atoms
    );

    /// <inheritdoc cref="ChoicesDelegate"/>
    public required ChoicesDelegate Choices { get; init; }

    /// <summary>
    /// Produces the changes for the picked choice, which will be passed to <see cref="Atoms.Write"/>.
    /// Returning an empty list still fires an event, but returning null will not.
    /// </summary>
    /// <param name="R">The atom reader to pull data.</param>
    /// <param name="atoms">The atoms that were targeted with this command.</param>
    /// <param name="value">The value of the choice picked.</param>
    /// <returns>The changes, written as one event.</returns>
    public delegate IEnumerable<IChange> EffectsDelegate(
        IAtomReader R,
        IReadOnlyList<T> atoms,
        int value
    );

    /// <inheritdoc cref="EffectsDelegate"/>
    public required EffectsDelegate Effects { get; init; }

    public bool NumberKeys { get; init; }

    public override bool Matches(InputEvent e) => base.Matches(e) || NumberPressed(e) != null;

    public IReadOnlyList<(string Label, int Value)> ChoicesFor(IReadOnlyList<AtomRef> targets) =>
        Resolved(targets) is { } atoms ? Choices(Atoms.R, atoms) : [];

    public int? NumberPressed(InputEvent e)
    {
        if (!NumberKeys)
            return null;
        for (int n = 1; n <= ShortcutActions.NumberCount; n++)
        {
            if (Shortcuts.Pressed(e, ShortcutActions.Number(n)))
                return n;
        }
        return null;
    }

    public void Choose(IReadOnlyList<AtomRef> targets, int value, ICommandView view)
    {
        if (Resolved(targets) is { } atoms)
            Submit(Effects(Atoms.R, atoms, value), view);
    }

    // Picking it opens its submenu.
    public override void Run(IReadOnlyList<AtomRef> targets, ICommandView view) { }
}

/// <summary>A command that does not act on selected targets, such as undo.</summary>
public sealed class GlobalCommand : Command
{
    public override bool CanActOn(IAtomReader R, AtomRef target) => false;

    public override bool Fits(int count) => count == 0;

    /// <summary>
    /// Define rules that must be met.
    /// Broken rules are displayed in the tooltip when the command is disabled.
    /// </summary>
    public IReadOnlyList<(string Reason, Func<IAtomReader, bool> Holds)> Requires { get; init; } =
    [];

    /// <summary>
    /// Runs whenever the user activates the command.
    /// </summary>
    /// <param name="R">The atom reader to pull data.</param>
    /// <param name="view">The view it runs in, or null outside any view.</param>
    /// <returns>
    /// The atom writes, written as one event.
    /// Null will not fire an event, but an empty collection will.
    /// </returns>
    public delegate IEnumerable<IChange> EffectsDelegate(IAtomReader R, ICommandView view);

    /// <inheritdoc cref="EffectsDelegate"/>
    public required EffectsDelegate Effects { get; init; }

    public override bool IsAvailable(IAtomReader R) => Requires.All(rule => rule.Holds(R));

    public override string DisabledReason(
        IAtomReader R,
        int count,
        IReadOnlyList<string> blocked
    ) => Requires.FirstOrDefault(rule => !rule.Holds(R)).Reason;

    public override void Run(IReadOnlyList<AtomRef> targets, ICommandView view)
    {
        if (IsAvailable(Atoms.R))
            Submit(Effects(Atoms.R, view), view);
    }
}

// This is just a sentinel so that we can put dividers in command lists.
file sealed class DividerCommand : Command
{
    public override bool CanActOn(IAtomReader R, AtomRef target) => false;

    public override bool Fits(int count) => count == 0;

    public override void Run(IReadOnlyList<AtomRef> targets, ICommandView view) { }
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
