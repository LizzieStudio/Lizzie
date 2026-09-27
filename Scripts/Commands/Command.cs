using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>
/// Identifies a command, like "component.flip".
/// </summary>
public readonly record struct CommandId(string Id);

/// <summary>How many targets a command can act on.</summary>
public enum TargetCount
{
    /// <summary>Doesn't handle any targets, such as the global Undo.</summary>
    None,
    /// <summary>Can only handle one target, such as Edit Prototype.</summary>
    One,
    /// <summary>Can handle many targets, such as Delete.</summary>
    Many,
}

/// <summary>What commands act on. Usually the selection or a hovered element.</summary>
public sealed class CommandContext
{
    /// <summary>
    /// Targets that are reachable through a context menu <strong>and</strong> keyboard commands.
    /// Usually the record being selected, like the component record when selecting a node.
    /// </summary>
    public ImmutableHashSet<Target> Primary { get; init; } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Targets that are reachable through a context menu, but not keyboard commands.
    /// For example, the delete key should delete a selected component record, but not its prototype.
    /// </summary>
    public ImmutableHashSet<Target> Secondary { get; init; } = ImmutableHashSet<Target>.Empty;
}

/// <summary>A window or panel that commands run in. It says what they act on.</summary>
public interface ICommandView
{
    /// <summary>What commands act on, or null while the view isn't taking commands.</summary>
    CommandContext BuildContext();
}

/// <summary>
/// Something the user runs from a context menu or a keyboard shortcut.
/// Every command is defined once, in <see cref="CommandList"/>.
/// </summary>
public abstract class Command
{
    public CommandId Id { get; init; }

    /// <summary>
    /// The menu label, which names the kind of target, e.g. "Edit Prototype".
    /// With a <see cref="Noun"/>, "{0}" is the counted targets: "Delete {0}" shows "Delete 5 Components".
    /// </summary>
    public string Caption { get; init; }

    /// <summary>What one target is called, and several, when the caption counts them.</summary>
    public (string One, string Many)? Noun { get; init; }

    /// <summary>The menu label for <paramref name="count"/> targets.</summary>
    public string Label(int count) =>
        Noun is var (one, many)
            ? string.Format(Caption, $"{count} {(count == 1 ? one : many)}")
            : Caption;

    /// <summary>The input action that runs it, or null.</summary>
    public StringName Shortcut { get; init; }

    public TargetCount Count { get; init; } = TargetCount.Many;

    public bool ShowInMenu { get; init; } = true;

    /// <summary>The menu asks how many through a submenu.</summary>
    public bool AsksQuantity { get; init; }

    /// <summary>The number keys run it, giving their number as the quantity.</summary>
    public bool NumberKeys { get; init; }

    /// <summary>The targets of the kind this command acts on.</summary>
    public abstract IEnumerable<Target> OfKind(IEnumerable<Target> targets);

    /// <summary>Whether it can act on this target of its kind, e.g. only decks shuffle.</summary>
    public virtual bool Applies(Target target) => true;

    /// <summary>
    /// Acts on the targets. <paramref name="quantity"/> is 1 unless the command asks for one,
    /// and <see cref="int.MaxValue"/> means all.
    /// </summary>
    public abstract void Run(IReadOnlyList<Target> targets, int quantity);

    /// <summary>
    /// Whether the key event runs this command, and the quantity it gives.
    /// Modifiers must match exactly.
    /// </summary>
    public bool Matches(InputEvent e, out int quantity)
    {
        quantity = 1;
        if (Shortcut != null && Shortcuts.Pressed(e, Shortcut))
            return true;
        if (!NumberKeys)
            return false;
        for (int n = 1; n <= 20; n++)
        {
            if (Shortcuts.Pressed(e, $"num_{n}"))
            {
                quantity = n;
                return true;
            }
        }
        return false;
    }

    /// <summary>The key to show beside it in a menu, or null.</summary>
    public Shortcut ShortcutLabel()
    {
        if (Shortcut == null || !InputMap.HasAction(Shortcut))
            return null;
        var key = InputMap.ActionGetEvents(Shortcut).OfType<InputEventKey>().FirstOrDefault();
        if (key == null)
            return null;

        // Bindings by physical key have no label of their own, so show the key it types.
        if (key.Keycode == Key.None && key.PhysicalKeycode != Key.None)
        {
            key = (InputEventKey)key.Duplicate();
            key.Keycode = DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode);
            key.PhysicalKeycode = Key.None;
        }
        return new Shortcut { Events = [key] };
    }
}

/// <summary>A command on table components, run through their nodes.</summary>
public sealed class ComponentCommand : Command
{
    /// <summary>Whether it can act on the component.</summary>
    public Func<VisualComponentBase, bool> AppliesTo { get; init; } = _ => true;

    /// <summary>Acts on the components with the quantity.</summary>
    public Action<List<VisualComponentBase>, int> Action { get; init; }

    public override IEnumerable<Target> OfKind(IEnumerable<Target> targets) =>
        targets.Where(t => Node(t) != null);

    public override bool Applies(Target target) => Node(target) is { } n && AppliesTo(n);

    public override void Run(IReadOnlyList<Target> targets, int quantity) =>
        Action(targets.Select(Node).Where(n => n != null).ToList(), quantity);

    private static VisualComponentBase Node(Target target) =>
        target is RecordTarget r ? ProjectService.Instance?.GameObjects?.GetComponent(r.Id) : null;
}

/// <summary>A command on records of one type.</summary>
public sealed class RecordCommand<T> : Command
    where T : class, IReplicated
{
    public Action<List<T>> Action { get; init; }

    public override IEnumerable<Target> OfKind(IEnumerable<Target> targets) =>
        targets.Where(t => Record(t) != null);

    public override void Run(IReadOnlyList<Target> targets, int quantity) =>
        Action(targets.Select(Record).Where(r => r != null).ToList());

    private static T Record(Target target) =>
        target is RecordTarget r ? ProjectService.Instance?.Get<T>(r.Id) : null;
}

/// <summary>A command with no targets, such as undo.</summary>
public sealed class GlobalCommand : Command
{
    public GlobalCommand() => Count = TargetCount.None;

    public Action Action { get; init; }

    public override IEnumerable<Target> OfKind(IEnumerable<Target> targets) => [];

    public override void Run(IReadOnlyList<Target> targets, int quantity) => Action();
}

/// <summary>
/// Utilities to attach <see cref="ICommandView"> to a <see cref="Node">.
/// </summary>
public static class CommandViews
{
    private static readonly Dictionary<Node, ICommandView> Attached = new();

    /// <summary>Makes <paramref name="view"/> take the commands for everything under <paramref name="node"/>.</summary>
    public static void Attach(Node node, ICommandView view) => Attached[node] = view;

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
