using System;
using System.Collections.Generic;

namespace Lizzie;

public static class Utility
{
    /// <summary>
    /// Returns all pairs of positive integers whose product equals the input value.
    /// Each pair is ordered with the smaller number first.
    /// </summary>
    /// <param name="input">The number to factorize.</param>
    /// <returns>An array of tuples containing all factor pairs and their ratios.</returns>
    public static (int f1, int f2, float ratio)[] FactorPairs(int input)
    {
        if (input <= 0)
            return Array.Empty<(int, int, float)>();

        var pairs = new List<(int, int, float)>();

        // Only need to check up to the square root for efficiency
        int sqrt = (int)Math.Sqrt(input);

        for (int i = 1; i <= sqrt; i++)
        {
            if (input % i == 0)
            {
                int complement = input / i;

                float ratio = (float)i / complement;
                pairs.Add((i, complement, ratio));
            }
        }

        return pairs.ToArray();
    }

    public static string ComponentTypeToScenePath(
        VisualComponentBase.VisualComponentType componentType,
        ComponentParameters parameters,
        IAtomReader R,
        bool isCard = false,
        bool previewMode = false
    )
    {
        switch (componentType)
        {
            case VisualComponentBase.VisualComponentType.Cube:
                return "res://Scenes/VisualComponents/VcCube.tscn";

            case VisualComponentBase.VisualComponentType.Disc:
                return "res://Scenes/VisualComponents/VcDisc.tscn";

            case VisualComponentBase.VisualComponentType.Printed:
            {
                if (!isCard && !previewMode)
                    return "res://Scenes/VisualComponents/VcDeck.tscn";
                return PrintedScene((TokenShape)((PrintedParameters)parameters).Shape);
            }

            case VisualComponentBase.VisualComponentType.Die:
                return DieScene((DieParameters)parameters, R);

            case VisualComponentBase.VisualComponentType.Mesh:
                break;

            case VisualComponentBase.VisualComponentType.Meeple:
                return "res://Scenes/VisualComponents/VcMeeple.tscn";

            case VisualComponentBase.VisualComponentType.Tray:
                return "res://Scenes/VisualComponents/VcTray.tscn";

            case VisualComponentBase.VisualComponentType.Bag:
                return "res://Scenes/VisualComponents/VcBag.tscn";

            case VisualComponentBase.VisualComponentType.Zone:
                return "res://Scenes/VisualComponents/VcZone.tscn";

            default:
                throw new ArgumentOutOfRangeException(nameof(componentType), componentType, null);
        }

        return string.Empty;
    }

    /// <summary>
    /// The scene for a printed component of <paramref name="shape"/>.
    /// </summary>
    public static string PrintedScene(TokenShape shape) =>
        shape switch
        {
            TokenShape.Circle => "res://Scenes/VisualComponents/VcPrintedCircle.tscn",
            TokenShape.HexPoint => "res://Scenes/VisualComponents/VcPrintedHexPoint.tscn",
            TokenShape.HexFlat => "res://Scenes/VisualComponents/VcPrintedHexFlat.tscn",
            _ => "res://Scenes/VisualComponents/VcPrinted.tscn",
        };

    /// <summary>
    /// The scene that shows a die.
    /// </summary>
    public static string DieScene(DieParameters die, IAtomReader R) =>
        DieParameters.Scenes[die.Resolve(R).Sides];
}
