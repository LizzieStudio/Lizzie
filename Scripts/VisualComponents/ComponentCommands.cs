using System.Collections.Generic;
using System.Linq;
using Godot;
using static Lizzie.VisualComponentBase;

namespace Lizzie;

/// <summary>
/// Commands for any table component.
/// Those that include contents act on a deck's cards and a container's contents as if selected,
/// so a deck needs no special handling here.
/// </summary>
public static class ComponentCommands
{
    [Command("component.flip")]
    public static readonly Command Flip = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.Flip,
        Caption = "Flip {0}",
        Keys = [Shortcuts.Key(Key.F)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = (R, c) => R.Kind(c) == VisualComponentType.Printed && !R.IsHidden(c),
        Effects = Flipped,
    };

    [Command("component.rotate_cw")]
    public static readonly Command RotateCw = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.RotateRight,
        Caption = "Rotate {0} Right",
        Keys = [Shortcuts.Key(Key.E)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Turnable,
        Effects = (R, cs) => cs.Select(c => Rotated(c, -RotationStep(R))),
    };

    [Command("component.rotate_ccw")]
    public static readonly Command RotateCcw = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.RotateLeft,
        Caption = "Rotate {0} Left",
        Keys = [Shortcuts.Key(Key.Q)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Turnable,
        Effects = (R, cs) => cs.Select(c => Rotated(c, RotationStep(R))),
    };

    [Command("component.move_to_top")]
    public static readonly Command MoveToTop = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.SendToTop,
        Caption = "Move {0} to Top",
        Keys = [Shortcuts.Key(Key.T)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs) => Reordered(cs, ZTarget.Top),
    };

    [Command("component.move_to_bottom")]
    public static readonly Command MoveToBottom = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.SendToBottom,
        Caption = "Move {0} to Bottom",
        Keys = [Shortcuts.Key(Key.B)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs) => Reordered(cs, ZTarget.Bottom),
    };

    [Command("component.delete")]
    public static readonly Command Delete = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.Delete,
        Caption = "Delete {0}",
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = (R, c) => !c.IsHeld,
        Effects = (R, cs) => cs.Select(c => c with { Removed = true }),
    };

    [Command("component.reset")]
    public static readonly Command Reset = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.Reset,
        Caption = "Reset {0}",
        Tooltip = "Moves a component back to where it is in the parent blueprint.",
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = (R, c) => !c.IsHeld,
        Requires = [("These components are already at their starting positions.", WrittenHere)],
        Effects = (R, cs) => cs.Select(R.Unset),
    };

    [Command("component.merge_into_blueprint")]
    public static readonly Command MergeIntoBlueprint = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.MergeIntoParent,
        Caption = "Merge {0} into Blueprint",
        Tooltip = "Moves this component, in its current position, into the parent blueprint.",
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = (R, c) =>
            !c.IsHeld && R.Get(Stage.ById, c.StageRef)?.ParentRef is { } p && !p.IsEmpty,
        Requires = [("These components have no changes to merge.", WrittenHere)],
        Effects = (R, cs) =>
            cs.SelectMany(c =>
                new IChange[]
                {
                    c with
                    {
                        StageRef = R.Get(Stage.ById, c.StageRef).ParentRef,
                    },
                    R.Unset(c),
                }
            ),
    };

    private static bool WrittenHere(IAtomReader R, ComponentPosition c) =>
        R.Get(ComponentPosition.WrittenIn, c.StageRef).Any(w => w.Id == c.Id);

    private static float RotationStep(IAtomReader R) =>
        R.Get(ProjectGameSettings.Only).RotationStep();

    // What's inside a bag isn't on the table to turn. A hand shows its cards turned as they'll lie on the table.
    private static bool Turnable(IAtomReader R, ComponentPosition c) => !R.IsHidden(c);

    // Zones stay underneath everything.
    private static bool Restackable(IAtomReader R, ComponentPosition c) =>
        (c.IsOnTable || R.IsInDeck(c)) && R.Kind(c) != VisualComponentType.Zone;

    /// <summary>
    /// The tokens turned over. Tokens stacked in the same place, or in the same deck, turn over together like a pile,
    /// reversing their order by swapping their ZOrders. Cards in a hand aren't stacked, so each turns over in place.
    /// <see cref="VcPrinted"/> animates what Flip writes.
    /// </summary>
    private static IEnumerable<ComponentPosition> Flipped(
        IAtomReader R,
        IEnumerable<ComponentPosition> tokens
    ) =>
        tokens
            .GroupBy(c =>
                c.Container is InHand
                    ? (c.Container, 0, 0, c.Id)
                    : (c.Container, c.X, c.Z, Ref<Component>.Empty)
            )
            .SelectMany(pile =>
            {
                var ordered = pile.OrderBy(c => c.ZOrder).ToList();
                return ordered.Select(
                    (c, i) => TurnedOver(c) with { ZOrder = ordered[^(i + 1)].ZOrder }
                );
            });

    private static ComponentPosition TurnedOver(ComponentPosition s)
    {
        bool faceUp = Mathf.RadToDeg(s.Rotation.Z) >= 90;
        return s with
        {
            Rotation = new Vector3(s.Rotation.X, s.Rotation.Y, Mathf.DegToRad(faceUp ? 0f : 180f)),
        };
    }

    /// <summary>The component turned about Y by <paramref name="degrees"/>.</summary>
    private static ComponentPosition Rotated(ComponentPosition s, float degrees) =>
        s with
        {
            Rotation = s.Rotation + new Vector3(0, Mathf.DegToRad(degrees), 0),
        };

    /// <summary>The components sent to the top or bottom of the ZOrder, keeping their order.</summary>
    private static IEnumerable<ComponentPosition> Reordered(
        IEnumerable<ComponentPosition> components,
        ZTarget target
    )
    {
        var ordered = components.OrderBy(s => s.ZOrder).ToList();
        var stamp = SnowportId.Create();
        return ordered.Select((s, i) => s with { ZOrder = new ZOrder(target, i, stamp) });
    }
}
