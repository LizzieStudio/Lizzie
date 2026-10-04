using System.Collections.Generic;
using System.Linq;
using Godot;
using static VisualComponentBase;

/// <summary>
/// Commands for any table component.
/// Those that include contents act on a deck's cards and a container's contents as if selected,
/// so a deck needs no special handling here.
/// </summary>
public static class ComponentCommands
{
    public static readonly Command Flip = new RecordCommand<ComponentState>
    {
        Name = new("component.flip"),
        Icon = CommandIcons.Flip,
        Caption = "Flip {0}",
        Keys = [Shortcuts.Key(Key.F)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = (R, c) => R.Kind(c) == VisualComponentType.Token && !R.IsContained(c),
        Effects = (R, cs, _) => Flipped(cs),
    };

    public static readonly Command RotateCw = new RecordCommand<ComponentState>
    {
        Name = new("component.rotate_cw"),
        Icon = CommandIcons.RotateRight,
        Caption = "Rotate {0} Right",
        Keys = [Shortcuts.Key(Key.E)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = NotContained,
        Effects = (R, cs, _) => cs.Select(c => Rotated(c, -RotationStep)),
    };

    public static readonly Command RotateCcw = new RecordCommand<ComponentState>
    {
        Name = new("component.rotate_ccw"),
        Icon = CommandIcons.RotateLeft,
        Caption = "Rotate {0} Left",
        Keys = [Shortcuts.Key(Key.Q)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = NotContained,
        Effects = (R, cs, _) => cs.Select(c => Rotated(c, RotationStep)),
    };

    public static readonly Command MoveToTop = new RecordCommand<ComponentState>
    {
        Name = new("component.move_to_top"),
        Icon = CommandIcons.SendToTop,
        Caption = "Move {0} to Top",
        Keys = [Shortcuts.Key(Key.T)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs, _) => Reordered(cs, ZTarget.Top),
    };

    public static readonly Command MoveToBottom = new RecordCommand<ComponentState>
    {
        Name = new("component.move_to_bottom"),
        Icon = CommandIcons.SendToBottom,
        Caption = "Move {0} to Bottom",
        Keys = [Shortcuts.Key(Key.B)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs, _) => Reordered(cs, ZTarget.Bottom),
    };

    public static readonly Command Delete = new RecordCommand<ComponentState>
    {
        Name = new("component.delete"),
        Icon = CommandIcons.Delete,
        Caption = "Delete {0}",
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = NotHeld,
        Effects = (R, cs, _) => cs.Select(c => c with { Deleted = true }),
    };

    private static float RotationStep => ProjectService.Instance.RotationStep;

    // Deleting or restacking what someone is dragging would fight the drag.
    private static bool NotHeld(IRecordReader R, ComponentState c) => !R.IsBeingDragged(c);

    // What's inside a bag or a hand isn't on the table to turn or restack.
    private static bool NotContained(IRecordReader R, ComponentState c) => !R.IsContained(c);

    // Zones stay underneath everything.
    private static bool Restackable(IRecordReader R, ComponentState c) =>
        c.IsOnTable && R.Kind(c) != VisualComponentType.Zone;

    /// <summary>
    /// The tokens turned over. Tokens stacked in the same place turn over together like a pile,
    /// reversing their order by swapping their ZOrders.
    /// <see cref="VcToken"/> animates what Flip writes.
    /// </summary>
    private static IEnumerable<ComponentState> Flipped(IEnumerable<ComponentState> tokens) =>
        tokens
            .GroupBy(c => (c.ContainerRef, c.X, c.Z))
            .SelectMany(pile =>
            {
                var ordered = pile.OrderBy(c => c.ZOrder).ToList();
                return ordered.Select(
                    (c, i) => TurnedOver(c) with { ZOrder = ordered[^(i + 1)].ZOrder }
                );
            });

    private static ComponentState TurnedOver(ComponentState s)
    {
        bool faceUp = Mathf.RadToDeg(s.Rotation.Z) >= 90;
        return s with
        {
            Rotation = new Vector3(s.Rotation.X, s.Rotation.Y, Mathf.DegToRad(faceUp ? 0f : 180f)),
        };
    }

    /// <summary>The component turned about Y by <paramref name="degrees"/>.</summary>
    private static ComponentState Rotated(ComponentState s, float degrees) =>
        s with
        {
            Rotation = s.Rotation + new Vector3(0, Mathf.DegToRad(degrees), 0),
        };

    /// <summary>The components sent to the top or bottom of the ZOrder, keeping their order.</summary>
    private static IEnumerable<ComponentState> Reordered(
        IEnumerable<ComponentState> components,
        ZTarget target
    )
    {
        var ordered = components.OrderBy(s => s.ZOrder).ToList();
        var stamp = Snowport.Clock.Create();
        return ordered.Select((s, i) => s with { ZOrder = new ZOrder(target, i, stamp) });
    }
}
