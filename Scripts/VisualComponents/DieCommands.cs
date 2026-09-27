using System;
using System.Linq;
using static VisualComponentBase;

/// <summary>Commands for dice.</summary>
public static class DieCommands
{
    // The component creation dialog's die icon.
    private const string DieIcon =
        "res://Textures/UI/ifl_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";

    public static readonly Command Roll = new RecordCommand<ComponentState>
    {
        Id = new("die.roll"),
        Icon = DieIcon,
        Caption = "Roll {0}",
        Noun = ("Die", "Dice"),
        Shortcut = "roll",
        AppliesTo = IsDie,
        Effects = (R, cs, _) => Effect.UpsertAll(cs.Select(c => Rolled(R, c))),
    };

    public static readonly Command SetFace = new RecordCommand<ComponentState>
    {
        Id = new("die.set_face"),
        Icon = DieIcon,
        Caption = "Set {0}",
        Noun = ("Die Face", "Die Faces"),
        AsksForNumber = true,
        NumberKeys = true,
        // Every face any of the dice has. A smaller die ignores the faces it lacks.
        NumberLimit = (R, dice) =>
            dice.Select(d => Parameters(R, d) is { } p ? VcDie.Faces(p).Length : 0)
                .DefaultIfEmpty()
                .Max(),
        AppliesTo = IsDie,
        Effects = (R, cs, n) =>
            Effect.UpsertAll(cs.Select(c => Showing(R, c, n)).Where(s => s != null)),
    };

    private static bool IsDie(IRecordReader R, ComponentState c) =>
        R.Kind(c) == VisualComponentType.Die;

    private static DieParameters Parameters(IRecordReader R, ComponentState die) =>
        R.GetIncludingDeleted<Prototype>(die.PrototypeRef)?.Parameters as DieParameters;

    /// <summary>
    /// The die rolled to a random face, animating the roll. The rolling player picks the face.
    /// </summary>
    private static ComponentState Rolled(IRecordReader R, ComponentState die)
    {
        var s = die.Settled() with { Transition = Transition.Roll };
        if (Parameters(R, die) is not { SideCount: > 0 } p)
            return s;
        int side = Random.Shared.Next(p.SideCount) + 1;
        return VcDie.FaceRotation(p, side) is { } rotation ? s with { Rotation = rotation } : s;
    }

    /// <summary>The die turned to show <paramref name="face"/>, or null if it has no such face.</summary>
    private static ComponentState Showing(IRecordReader R, ComponentState die, int face) =>
        Parameters(R, die) is { } p && VcDie.FaceRotation(p, face) is { } rotation
            ? die.Settled() with
            {
                Rotation = rotation,
            }
            : null;
}
