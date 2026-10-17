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
    public static readonly Command Flip = new AtomCommand<ComponentState>
    {
        Name = new("component.flip"),
        Icon = CommandIcons.Flip,
        Caption = "Flip {0}",
        Keys = [Shortcuts.Key(Key.F)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = (R, c) => R.Kind(c) == VisualComponentType.Token && !R.IsHidden(c),
        Effects = (R, cs, _) => Flipped(R, cs),
    };

    public static readonly Command RotateCw = new AtomCommand<ComponentState>
    {
        Name = new("component.rotate_cw"),
        Icon = CommandIcons.RotateRight,
        Caption = "Rotate {0} Right",
        Keys = [Shortcuts.Key(Key.E)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Turnable,
        Effects = (R, cs, _) => cs.Select(c => Rotated(c, -RotationStep(R))),
    };

    public static readonly Command RotateCcw = new AtomCommand<ComponentState>
    {
        Name = new("component.rotate_ccw"),
        Icon = CommandIcons.RotateLeft,
        Caption = "Rotate {0} Left",
        Keys = [Shortcuts.Key(Key.Q)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Turnable,
        Effects = (R, cs, _) => cs.Select(c => Rotated(c, RotationStep(R))),
    };

    public static readonly Command MoveToTop = new AtomCommand<ComponentState>
    {
        Name = new("component.move_to_top"),
        Icon = CommandIcons.SendToTop,
        Caption = "Move {0} to Top",
        Keys = [Shortcuts.Key(Key.T)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs, _) => Reordered(cs, ZTarget.Top),
    };

    public static readonly Command MoveToBottom = new AtomCommand<ComponentState>
    {
        Name = new("component.move_to_bottom"),
        Icon = CommandIcons.SendToBottom,
        Caption = "Move {0} to Bottom",
        Keys = [Shortcuts.Key(Key.B)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs, _) => Reordered(cs, ZTarget.Bottom),
    };

    public static readonly Command Delete = new AtomCommand<ComponentState>
    {
        Name = new("component.delete"),
        Icon = CommandIcons.Delete,
        Caption = "Delete {0}",
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = NotHeld,
        Effects = (R, cs, _) => cs.Select(c => c with { Removed = true }),
    };

    private static float RotationStep(IAtomReader R) =>
        R.Get(ProjectGameSettings.Only).RotationStep();

    // Deleting or restacking what someone is dragging would fight the drag.
    private static bool NotHeld(IAtomReader R, ComponentState c) => !R.IsBeingDragged(c);

    // What's inside a bag isn't on the table to turn. A hand shows its cards turned as they'll lie on the table.
    private static bool Turnable(IAtomReader R, ComponentState c) => !R.IsHidden(c);

    // Zones stay underneath everything.
    private static bool Restackable(IAtomReader R, ComponentState c) =>
        (c.IsOnTable || R.IsInDeck(c)) && R.Kind(c) != VisualComponentType.Zone;

    /// <summary>
    /// The tokens turned over. Tokens stacked in the same place, or in the same deck, turn over together like a pile,
    /// reversing their order by swapping their ZOrders. Cards in a hand aren't stacked, so each turns over in place.
    /// <see cref="VcToken"/> animates what Flip writes.
    /// </summary>
    private static IEnumerable<ComponentState> Flipped(
        IAtomReader R,
        IEnumerable<ComponentState> tokens
    ) =>
        tokens
            .GroupBy(c =>
                R.IsInHand(c)
                    ? (c.ContainerRef, 0, 0, c.Id)
                    : (c.ContainerRef, c.X, c.Z, SnowTag.Empty)
            )
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
