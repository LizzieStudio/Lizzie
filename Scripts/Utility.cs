using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

public partial class Utility : Node
{
    public static Utility Instance { get; private set; }

    private TokenTextureSubViewport _textureCreator;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        _textureCreator = GetNode<TokenTextureSubViewport>("TextureCreator");
    }

    public Texture2D CreateQuickTexture(TokenTextureParameters parameters)
    {
        return _textureCreator.CreateQuickTexture(parameters);
    }

    public float GetAabbSize(Aabb aabb)
    {
        return aabb.GetLongestAxisSize();
    }

    /// <summary>
    /// This function parses a string like "1-6, SKIP, +2" into a string array.
    /// (which in this case would be 1, 2, 3, 4, 5, 6, SKIP, +2)
    /// Also can understand single character ranges like "A-J"
    /// </summary>
    /// <param name="input">string to parse</param>
    /// <returns>resulting array</returns>
    public static string[] ParseValueRanges(string input)
    {
        if (string.IsNullOrEmpty(input))
            return Array.Empty<string>();
        var sInput = input.Replace(" ", string.Empty);

        var pages = new List<string>();
        var ranges = sInput.Split(',');

        foreach (var range in ranges)
        {
            if (range.Contains('-'))
            {
                var sBound = range.Split('-');
                if (sBound.Length != 2)
                    continue;

                if (int.TryParse(sBound[0], out var low) && int.TryParse(sBound[1], out var high))
                {
                    if (high < low)
                    {
                        var r = Enumerable.Range(high, low - high + 1).ToArray().Reverse();
                        pages.AddRange(r.Select(s => s.ToString()));
                    }
                    else
                    {
                        pages.AddRange(
                            Enumerable
                                .Range(low, high - low + 1)
                                .ToArray()
                                .Select(s => s.ToString())
                        );
                    }
                }
                else if (sBound[0].Length == 1 && sBound[1].Length == 1)
                {
                    //ASCII single character mode
                    char c1 = sBound[0][0];
                    char c2 = sBound[1][0];

                    if (char.IsAscii(c1) && char.IsAscii(c2))
                    {
                        var a1 = (int)c1;
                        var a2 = (int)c2;

                        if (a1 > a2)
                            (a1, a2) = (a2, a1);

                        for (int i = a1; i <= a2; i++)
                        {
                            pages.Add(((char)i).ToString());
                        }
                    }
                }
                else
                {
                    //just give up and add the original string
                    pages.Add(range);
                }
            }
            else
            {
                pages.Add(range);
            }
        }

        return pages.ToArray();
    }

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

    public static float PixelSize(Vector2 size)
    {
        if (size.X == 0 || size.Y == 0)
            return 0;

        return 0.95f / Mathf.Max(size.X, size.Y);
    }

    public static ImageTexture LoadTexture(string filename)
    {
        var image = new Image();
        var err = image.Load(filename);
        GD.Print(err);

        if (err == Error.Ok)
        {
            var texture = new ImageTexture();
            texture.SetImage(image);
            return texture;
        }

        return new ImageTexture();
    }

    public static string ComponentTypeToScenePath(
        VisualComponentBase.VisualComponentType componentType,
        ComponentParameters parameters,
        int rowIndex = -1,
        SnowTag? rowId = null,
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
                bool isCard = rowIndex >= 0 || (rowId != null && rowId != SnowTag.Empty);
                if (!isCard && !previewMode)
                    return "res://Scenes/VisualComponents/VcDeck.tscn";
                return TokenScene(parameters);
            }

            case VisualComponentBase.VisualComponentType.Die:
                return DieScene(parameters);

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

    private static string DieScene(ComponentParameters parameters)
    {
        var sides = parameters is DieParameters die
            ? die.Sides
            : ImmutableArray<QuickTextureField>.Empty;

        if (sides.Length == 0)
            return $"res://Scenes/VisualComponents/Dice/VcD6s.tscn";

        string shape = string.Empty;

        switch (sides.Length)
        {
            case 4:
                shape = "vc_d_4.tscn";
                break;

            case 6:
                shape = "VcD6s.tscn";
                break;

            case 8:
                shape = "VcD8.tscn";
                break;

            case 10:
                shape = "VcD10.tscn";
                break;

            case 12:
                shape = "VcD12.tscn";
                break;

            case 20:
                shape = "VcD20.tscn";
                break;

            default:
                return string.Empty;
        }

        return $"res://Scenes/VisualComponents/Dice/{shape}";
    }
}
