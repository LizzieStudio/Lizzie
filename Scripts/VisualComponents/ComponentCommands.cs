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
    // The component creation dialog's icon for printed components, like tokens and cards.
    private const string TokenIcon =
        "res://Textures/UI/crop_portrait_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";

    private const string FlipIcon = "res://Textures/UI/flip.svg";
    private const string RotateRightIcon = "res://Textures/UI/rotate_right.svg";
    private const string RotateLeftIcon = "res://Textures/UI/rotate_left.svg";
    private const string SendToTopIcon = "res://Textures/UI/send_to_top.svg";
    private const string SendToBottomIcon = "res://Textures/UI/send_to_bottom.svg";
    private const string DeleteIcon = "res://Textures/UI/delete.svg";

    public static readonly Command Flip = new RecordCommand<ComponentState>
    {
        Id = new("component.flip"),
        Icon = FlipIcon,
        Caption = "Flip {0}",
        Keys = [Shortcuts.Key(Key.F)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = (R, c) => R.Kind(c) == VisualComponentType.Token && !c.IsContained,
        Effects = (R, cs, _) => Effect.UpsertAll(Flipped(cs)),
    };

    public static readonly Command RotateCw = new RecordCommand<ComponentState>
    {
        Id = new("component.rotate_cw"),
        Icon = RotateRightIcon,
        Caption = "Rotate {0} Right",
        Keys = [Shortcuts.Key(Key.E)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = NotContained,
        Effects = (R, cs, _) => Effect.UpsertAll(cs.Select(c => Rotated(c, -RotationStep))),
    };

    public static readonly Command RotateCcw = new RecordCommand<ComponentState>
    {
        Id = new("component.rotate_ccw"),
        Icon = RotateLeftIcon,
        Caption = "Rotate {0} Left",
        Keys = [Shortcuts.Key(Key.Q)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = NotContained,
        Effects = (R, cs, _) => Effect.UpsertAll(cs.Select(c => Rotated(c, RotationStep))),
    };

    public static readonly Command MoveToTop = new RecordCommand<ComponentState>
    {
        Id = new("component.move_to_top"),
        Icon = SendToTopIcon,
        Caption = "Move {0} to Top",
        Keys = [Shortcuts.Key(Key.T)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs, _) => Effect.UpsertAll(Reordered(cs, ZTarget.Top)),
    };

    public static readonly Command MoveToBottom = new RecordCommand<ComponentState>
    {
        Id = new("component.move_to_bottom"),
        Icon = SendToBottomIcon,
        Caption = "Move {0} to Bottom",
        Keys = [Shortcuts.Key(Key.B)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = Restackable,
        Effects = (R, cs, _) => Effect.UpsertAll(Reordered(cs, ZTarget.Bottom)),
    };

    public static readonly Command Delete = new RecordCommand<ComponentState>
    {
        Id = new("component.delete"),
        Icon = DeleteIcon,
        Caption = "Delete {0}",
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Contents,
        AppliesTo = NotHeld,
        Effects = (R, cs, _) =>
            Effect.UpsertAll(cs.Select(c => c.Settled() with { Deleted = true })),
    };

    private static float RotationStep => ProjectService.Instance.RotationStep;

    // Deleting or restacking what someone is dragging would fight the drag.
    private static bool NotHeld(IRecordReader _, ComponentState c) => !c.IsHeld;

    // What's inside a bag or a hand isn't on the table to turn or restack.
    private static bool NotContained(IRecordReader _, ComponentState c) => !c.IsContained;

    // Zones stay underneath everything.
    private static bool Restackable(IRecordReader R, ComponentState c) =>
        !c.IsHeld && !c.IsContained && R.Kind(c) != VisualComponentType.Zone;

    /// <summary>
    /// The tokens turned over. Tokens stacked in the same place turn over together like a pile,
    /// reversing their order by swapping their ZOrders.
    /// </summary>
    private static IEnumerable<ComponentState> Flipped(IEnumerable<ComponentState> tokens) =>
        tokens
            .GroupBy(c => (c.Location, c.Holder, c.X, c.Z))
            .SelectMany(pile =>
            {
                var ordered = pile.OrderBy(c => c.ZOrder).ToList();
                return ordered.Select(
                    (c, i) => TurnedOver(c) with { ZOrder = ordered[^(i + 1)].ZOrder }
                );
            });

    /// <summary>The token turned over, animating the flip.</summary>
    private static ComponentState TurnedOver(ComponentState s)
    {
        bool faceUp = Mathf.RadToDeg(s.Rotation.Z) >= 90;
        return s.Settled() with
        {
            Rotation = new Vector3(s.Rotation.X, s.Rotation.Y, Mathf.DegToRad(faceUp ? 0f : 180f)),
            Transition = Transition.Flip,
        };
    }

    /// <summary>The component turned about Y by <paramref name="degrees"/>.</summary>
    private static ComponentState Rotated(ComponentState s, float degrees) =>
        s.Settled() with
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
        return ordered.Select((s, i) => s.Settled() with { ZOrder = new ZOrder(target, i, stamp) });
    }
}
