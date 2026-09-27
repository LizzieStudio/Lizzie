using System.Collections.Generic;
using System.Linq;

/// <summary>Commands for any table component.</summary>
public static partial class CommandList
{
    public static readonly Command FlipComponent = new ComponentCommand
    {
        Id = new("component.flip"),
        Caption = "Flip {0}",
        Noun = ("Component", "Components"),
        Shortcut = "flip",
        AppliesTo = c => c is VcToken or VcDeck,
        Action = (cs, _) => Send(VisualCommand.Flip, cs),
    };

    public static readonly Command RotateComponentCw = new ComponentCommand
    {
        Id = new("component.rotate_cw"),
        Caption = "Rotate {0} CW",
        Noun = ("Component", "Components"),
        Shortcut = "rotate_cw",
        Action = (cs, _) => Send(VisualCommand.RotateCw, cs),
    };

    public static readonly Command RotateComponentCcw = new ComponentCommand
    {
        Id = new("component.rotate_ccw"),
        Caption = "Rotate {0} CCW",
        Noun = ("Component", "Components"),
        Shortcut = "rotate_ccw",
        Action = (cs, _) => Send(VisualCommand.RotateCcw, cs),
    };

    public static readonly Command MoveComponentToTop = new ComponentCommand
    {
        Id = new("component.move_to_top"),
        Caption = "Move {0} to Top",
        Noun = ("Component", "Components"),
        Shortcut = "move_to_top",
        AppliesTo = NotDragging,
        Action = (cs, _) => Table.Reorder(cs, ZTarget.Top),
    };

    public static readonly Command MoveComponentToBottom = new ComponentCommand
    {
        Id = new("component.move_to_bottom"),
        Caption = "Move {0} to Bottom",
        Noun = ("Component", "Components"),
        Shortcut = "move_to_bottom",
        AppliesTo = NotDragging,
        Action = (cs, _) => Table.Reorder(cs, ZTarget.Bottom),
    };

    public static readonly Command DuplicateComponent = new ComponentCommand
    {
        Id = new("component.duplicate"),
        Caption = "Duplicate {0}",
        Noun = ("Component", "Components"),
        Action = (cs, _) => cs.ForEach(c => c.ProcessCommand(VisualCommand.Duplicate)),
    };

    public static readonly Command DeleteComponent = new ComponentCommand
    {
        Id = new("component.delete"),
        Caption = "Delete {0}",
        Noun = ("Component", "Components"),
        Shortcut = "component_delete",
        AppliesTo = NotDragging,
        Action = (cs, _) => Table.DeleteComponents(cs),
    };

    public static readonly Command ZoomToComponent = new ComponentCommand
    {
        Id = new("component.zoom"),
        Caption = "Zoom to Component",
        Shortcut = "component_zoom",
        Count = TargetCount.One,
        Action = (cs, _) => CameraManager.Instance?.ZoomTo(cs[0]),
    };

    private static GameObjects Table => ProjectService.Instance.GameObjects;

    // Deleting or restacking what's being dragged would fight the drag.
    private static bool NotDragging(VisualComponentBase _) => Table.CursorMode != CursorMode.Drag;

    /// <summary>Sends the command to each component and submits their effects as one event.</summary>
    private static void Send(
        VisualCommand command,
        IEnumerable<VisualComponentBase> components,
        int? quantity = null
    )
    {
        var effects = components
            .SelectMany(c =>
                quantity is int n ? c.ProcessCommandWithQuantity(command, n) : c.ProcessCommand(command)
            )
            .ToArray();
        if (effects.Length > 0)
            EventSynchronizer.Instance?.Submit(TableEvent.Now(ActionFor(command), effects));
    }

    private static TableAction ActionFor(VisualCommand command)
    {
        // Number keys draw that many cards off a deck, which still works.
        // TODO We need to decouple drawing cards from setting the die face somehow.
        if ((int)command >= (int)VisualCommand.Num1 && (int)command <= (int)VisualCommand.Num20)
            return new DrawAction();

        return command switch
        {
            VisualCommand.Flip => new FlipAction(),
            VisualCommand.Roll => new RollAction(),
            VisualCommand.Shuffle => new ShuffleAction(),
            VisualCommand.Draw => new DrawAction(),
            VisualCommand.Deal => new DealAction(),
            _ => null,
        };
    }
}
