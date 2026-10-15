using System;
using System.Collections.Generic;

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
        IRecordReader R,
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

            case VisualComponentBase.VisualComponentType.Token:
                return TokenScene(parameters);

            case VisualComponentBase.VisualComponentType.Deck:
            {
                if (!isCard && !previewMode)
                    return "res://Scenes/VisualComponents/VcDeck.tscn";
                return TokenScene(parameters);
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

    private static string TokenScene(ComponentParameters parameters)
    {
        var shape = string.Empty;

        var si = parameters is PrintedParameters printed ? printed.Shape : 0;

        switch (si)
        {
            case 1:
                shape = "VcTokenCircle.tscn";
                break;

            case 2:
                shape = "VcTokenHexPoint.tscn";
                break;

            case 3:
                shape = "VcTokenHexFlat.tscn";
                break;

            default:
                shape = "VcToken.tscn";
                break;
        }

        return $"res://Scenes/VisualComponents/{shape}";
    }

    /// <summary>
    /// The scene that shows a die.
    /// </summary>
    public static string DieScene(DieParameters die, IRecordReader R) =>
        DieParameters.Scenes[die.Resolve(R).Sides];
}
