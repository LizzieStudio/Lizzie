using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>
/// Identifies a command, like "component.flip".
/// </summary>
public readonly record struct CommandId(string Id);

/// <summary>How many targets a <see cref="RecordCommand{T}"/> can act on.</summary>
public enum TargetCount
{
    /// <summary>Can only handle one target, such as Edit Prototype.</summary>
    One,

    /// <summary>Can handle many targets, such as Delete.</summary>
    Many,
}

/// <summary>What commands act on. Usually the selection or a hovered element.</summary>
public sealed class CommandContext
{
    /// <summary>
    /// What the user targeted. Usually the selection, but can also be the hovered target.
    /// Matching commands will act on these.
    /// </summary>
    public ImmutableHashSet<Target> Selected { get; init; } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Records that are contained within the selected targets, like a deck's cards or a DataSet's rows.
    /// Matching commands will act on these if configured to do so with <see cref="Command.IncludesContents"/>.
    /// </summary>
    public ImmutableHashSet<Target> Contents { get; init; } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// What the selected targets refer to, like a component's prototype.
    /// Matching commands will only act on these if triggered from the context menu, not from keyboard shortcuts.
    /// </summary>
    public ImmutableHashSet<Target> Referenced { get; init; } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Custom commands for this viewport, like the table's Zoom to Component.
    /// They appear the same as other commands.
    /// </summary>
    public IReadOnlyList<Command> Local { get; init; } = [];
}

/// <summary>
/// A window or panel that commands run in.
/// </summary>
public interface ICommandView
{
    /// <summary>
    /// The selection context for commands, or null while the view isn't taking commands.
    /// A view with nothing to act on leaves it empty.
    /// </summary>
    CommandContext BuildContext() => new();

    /// <summary>
    /// Whether an undo or redo issued here should target <paramref name="effect"/>.
    /// If the <paramref name="effect"/> is targeting records managed here, return true.
    /// Returning false will cause the next <see cref="Effect"/> to be checked until a valid target is found or the UndoLog runs out.
    /// </summary>
    bool UndoScope(Effect effect);
}

/// <summary>
/// Something the user runs from a context menu or a keyboard shortcut.
/// Every command is defined once, in a class for what it acts on, and listed in <see cref="CommandList"/>.
/// </summary>
public abstract class Command
{
    public CommandId Id { get; init; }

    /// <summary>
    /// The menu label, e.g. "Edit Prototype".
    /// "{0}" is the number of targets: "Delete {0}" shows "Delete 5".
    /// With a <see cref="Noun"/>, it's followed by what they are: "Delete 5 Components".
    /// </summary>
    public string Caption { get; init; }

    /// <summary>What one target is called, and several, after the count in the caption. Null for none.</summary>
    public (string One, string Many)? Noun { get; init; }

    /// <summary>The menu label for <paramref name="count"/> targets.</summary>
    public string Label(int count) =>
        string.Format(
            Caption,
            Noun is var (one, many) ? $"{count} {(count == 1 ? one : many)}" : count.ToString()
        );

    /// <summary>
    /// The keys that run it, made with <see cref="Shortcuts.Key"/> or <see cref="Shortcuts.Ctrl"/>.
    /// The first one shows in menus.
    /// </summary>
    public IReadOnlyList<InputEventKey> Keys { get; init; } = [];

    private StringName _action;

    /// <summary>
    /// The <see cref="InputMap"/> action holding its <see cref="Keys"/>, named by its id,
    /// so the keys can be changed while the game runs. Null when it has no keys.
    /// </summary>
    public StringName Action => Keys.Count == 0 ? null : _action ??= Id.Id;

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
    /// Set to true to include <see cref="CommandContext.Contents"/> in the targets.
    /// </summary>
    public bool IncludesContents { get; init; }

    /// <summary>
    /// The path of an icon shown in the menu's left column, or null for none.
    /// Commands for one kind of component use the icon the component creation dialog shows for it.
    /// </summary>
    public string Icon { get; init; }

    /// <summary>
    /// Returns true if this command can act on <paramref name="target"/>:
    /// a record of the kind it's for, that it has behavior for.
    /// </summary>
    public abstract bool Applies(Target target);

    /// <summary>Whether it can act on <paramref name="count"/> targets that it applies to.</summary>
    public abstract bool Fits(int count);

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
}

/// <summary>A command on records of one type.</summary>
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
    /// Generates the effects from this command on the given records.
    /// Returning null or an empty collection will not fire an event.
    /// The third parameter provides a number if <see cref="Command.AsksForNumber"/> is true, like with setting a die face.
    /// </summary>
    public EffectsDelegate Effects { get; init; }

    /// <summary>
    /// Run so that this command can perform actions other than change records, such as opening an editor.
    /// The second parameter provides a number if <see cref="Command.AsksForNumber"/> is true, like with setting a die face.
    /// </summary>
    public Action<IReadOnlyList<T>, int> SideEffects { get; init; }

    /// <summary>
    /// When <see cref="Command.AsksForNumber"/> is true, the highest number worth offering for the records,
    /// such as a die's face count or a deck's card count. Null offers the menu's default.
    /// </summary>
    public Func<IRecordReader, IReadOnlyList<T>, int> NumberLimit { get; init; }

    public override bool Applies(Target target) => Record(target) is { } r && AppliesTo(Reader, r);

    public override bool Fits(int count) => Count == TargetCount.One ? count == 1 : count > 0;

    public override int? MaxNumber(IReadOnlyList<Target> targets) =>
        NumberLimit?.Invoke(Reader, Records(targets));

    public override void Run(IReadOnlyList<Target> targets, int number, ICommandView view)
    {
        var records = Records(targets);
        SideEffects?.Invoke(records, number);
        if (Effects == null)
            return;

        var effects = Effects(Reader, records, number)?.ToArray() ?? [];
        if (effects.Length > 0)
            EventSynchronizer.Instance?.Submit(TableEvent.Now(null, effects));
    }

    private static IRecordReader Reader => ProjectService.Instance;

    private static T Record(Target target) =>
        target is RecordTarget r ? Reader?.Get<T>(r.Id) : null;

    // A record may be gone since the menu opened.
    private static List<T> Records(IReadOnlyList<Target> targets) =>
        targets.Select(Record).Where(r => r != null).ToList();
}

/// <summary>A command that does not act on selected targets, such as undo.</summary>
public sealed class GlobalCommand : Command
{
    public override bool Applies(Target target) => false;

    public override bool Fits(int count) => count == 0;

    /// <summary>What it does, given the view it runs in, or null outside any view.</summary>
    public Action<ICommandView> SideEffects { get; init; }

    public override void Run(IReadOnlyList<Target> targets, int number, ICommandView view) =>
        SideEffects(view);
}

/// <summary>
/// Utilities to attach <see cref="ICommandView"/> to a <see cref="Node"/>.
/// </summary>
public static class CommandViews
{
    private static readonly Dictionary<Node, ICommandView> Attached = new();

    /// <summary>Makes <paramref name="view"/> take the commands for everything under <paramref name="node"/>.</summary>
    public static void Attach(Node node, ICommandView view) => Attached[node] = view;

    /// <summary>
    /// Makes everything under <paramref name="node"/> a view with nothing to act on,
    /// where undo walks through <paramref name="undoScope"/>.
    /// </summary>
    public static void Attach(Node node, Func<Effect, bool> undoScope) =>
        Attach(node, new ScopeView(undoScope));

    private sealed class ScopeView(Func<Effect, bool> undoScope) : ICommandView
    {
        public bool UndoScope(Effect fx) => undoScope(fx);
    }

    public static void Detach(Node node) => Attached.Remove(node);

    /// <summary>The view for the viewport's focused control, or null if it has none.</summary>
    public static ICommandView Find(Viewport viewport)
    {
        for (Node n = viewport.GuiGetFocusOwner() ?? (Node)viewport; n != null; n = n.GetParent())
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
