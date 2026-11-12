using System;
using System.Linq;
using Godot;
using static Lizzie.VisualComponentBase;

namespace Lizzie;

/// <summary>Commands for dice.</summary>
public static class DieCommands
{
    [Command("die.roll")]
    public static readonly Command Roll = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.Die,
        Caption = "Roll {0}",
        Noun = ("Die", "Dice"),
        Keys = [Shortcuts.Key(Key.R)],
        AppliesTo = (R, c) => R.Kind(c) == VisualComponentType.Die,
        Effects = (R, cs) => cs.Select(c => Rolled(R, c)),
    };

    [Command("die.set_face")]
    public static readonly Command SetFace = new ChoiceCommand<ComponentPosition>
    {
        Icon = CommandIcons.SetFace,
        Caption = "Set {0}",
        Noun = ("Die Face", "Die Faces"),
        NumberKeys = true,
        AppliesTo = (R, c) => R.Kind(c) == VisualComponentType.Die,
        // Every face any of the dice has. A smaller die ignores the faces it lacks.
        Choices = (R, dice) =>
            [
                .. Enumerable
                    .Range(
                        1,
                        dice.Max(d =>
                            Parameters(R, d) is { } p ? VcDie.FaceRotations(p, R).Length : 0
                        )
                    )
                    .Select(n => (n.ToString(), n)),
            ],
        Effects = (R, cs, n) => cs.Select(c => Showing(R, c, n)).Where(s => s != null),
    };

    private static DieParameters Parameters(IAtomReader R, ComponentPosition die) =>
        R.GetPrototype(die)?.Parameters as DieParameters;

    /// <summary>
    /// The die rolled to a random face. The rolling player picks the face.
    /// <see cref="VcDie"/> animates what Roll writes, even when it lands on the same face.
    /// </summary>
    private static ComponentPosition Rolled(IAtomReader R, ComponentPosition die)
    {
        if (Parameters(R, die) is not { } p)
            return die;
        int side = Random.Shared.Next(VcDie.FaceRotations(p, R).Length) + 1;
        return VcDie.FaceRotation(p, side, R) is { } rotation
            ? die with
            {
                Rotation = rotation,
            }
            : die;
    }

    /// <summary>The die turned to show <paramref name="face"/>, or null if it has no such face.</summary>
    private static ComponentPosition Showing(IAtomReader R, ComponentPosition die, int face) =>
        Parameters(R, die) is { } p && VcDie.FaceRotation(p, face, R) is { } rotation
            ? die with
            {
                Rotation = rotation,
            }
            : null;
}
