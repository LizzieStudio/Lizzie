using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Utilities to create an in-memory template and dataset for quick dice.
/// </summary>
public static class DieTemplate
{
    /// <summary>
    /// Where one face sits on the net, in pixels of a <see cref="Net"/>-sized image.
    /// </summary>
    private record Face(int X, int Y, int Rotation);

    /// <summary>
    /// A die's net dimensions, in pixels.
    /// </summary>
    private record Net(int Width, int Height, int FaceWidth, int FaceHeight, Face[] Faces);

    private static readonly Dictionary<int, Net> Nets = new()
    {
        [6] = new(
            256,
            170,
            85,
            85,
            [
                new(42, 42, 0),
                new(127, 42, 0),
                new(212, 42, 0),
                new(42, 127, 0),
                new(127, 127, 0),
                new(212, 127, 0),
            ]
        ),
        [8] = new(
            256,
            256,
            110,
            110,
            [
                new(0, 55, 90),
                new(0, 165, 90),
                new(127, 201, -90),
                new(127, 92, -90),
                new(127, 55, 90),
                new(127, 165, 90),
                new(255, 201, -90),
                new(255, 92, -90),
            ]
        ),
        [10] = new(
            256,
            256,
            49,
            71,
            [
                new(48, 50, 0),
                new(128, 50, 0),
                new(212, 50, 0),
                new(169, 90, 180),
                new(88, 90, 180),
                new(48, 169, 0),
                new(128, 169, 0),
                new(212, 169, 0),
                new(169, 209, 180),
                new(88, 209, 180),
            ]
        ),
        [12] = new(
            256,
            256,
            47,
            53,
            [
                new(29, 89, 90),
                new(29, 156, 90),
                new(29, 223, 90),
                new(93, 89, 90),
                new(93, 156, 90),
                new(93, 223, 90),
                new(157, 89, 90),
                new(157, 156, 90),
                new(157, 223, 90),
                new(221, 89, 90),
                new(221, 156, 90),
                new(221, 223, 90),
            ]
        ),
        [20] = new(
            256,
            256,
            82,
            82,
            [
                new(0, 41, 90),
                new(64, 41, 90),
                new(128, 41, 90),
                new(192, 41, 90),
                new(64, 82, -90),
                new(128, 82, -90),
                new(192, 82, -90),
                new(255, 82, -90),
                new(0, 123, 90),
                new(64, 123, 90),
                new(128, 123, 90),
                new(192, 123, 90),
                new(64, 164, -90),
                new(128, 164, -90),
                new(192, 164, -90),
                new(255, 164, -90),
                new(0, 205, 90),
                new(64, 205, 90),
                new(128, 205, 90),
                new(192, 205, 90),
            ]
        ),
    };

    // An icon on a square or kite face keeps a margin; one on a triangle is fitted inside it instead.
    private const float IconScale = 0.8f;

    private const string Background = "Background";

    // Each face has four values, after the background.
    private static string Text(int face) => $"Text{face + 1}";

    private static string Icon(int face) => $"Icon{face + 1}";

    private static string Quantity(int face) => $"Quantity{face + 1}";

    private static string Foreground(int face) => $"Foreground{face + 1}";

    // How many of a net's pixels make an inch of the drawn die template.
    private const float NetPixelsPerInch = 50;

    /// <summary>
    /// The size a die template is drawn at, in inches, or (0, 0) for a die with no net.
    /// </summary>
    public static (float Width, float Height) Size(int sideCount) =>
        Nets.TryGetValue(sideCount, out var net)
            ? (net.Width / NetPixelsPerInch, net.Height / NetPixelsPerInch)
            : default;

    public static Template Template(int sides)
    {
        var net = Nets[sides];
        var template = new Template
        {
            Id = SnowTag.Empty,
            Name = $"Quick D{sides}",
            SizeTemplate = $"D{sides}",
        };
        bool triangles = template.TriangleFaces;
        string Percent(float pixels, int size) =>
            (pixels * 100 / size).ToString("0.##", CultureInfo.InvariantCulture);

        IEnumerable<ImmutableDictionary<string, string>> FaceElements(Face face, int n)
        {
            (string, string)[] Place(float scale) =>
                [
                    ("X", Percent(face.X, net.Width)),
                    ("Y", Percent(face.Y, net.Height)),
                    ("Width", Percent(net.FaceWidth * scale, net.Width)),
                    ("Height", Percent(net.FaceHeight * scale, net.Height)),
                    ("Rotation", face.Rotation.ToString(CultureInfo.InvariantCulture)),
                ];
            yield return QuickTemplate.Element([
                ("Type", "Text"),
                .. Place(1),
                ("Text", $"{{{Text(n)}}}"),
                ("Foreground", $"{{{Foreground(n)}}}"),
                ("Autosize", "True"),
            ]);
            yield return QuickTemplate.Element([
                ("Type", "Image"),
                .. Place(triangles ? 1 : IconScale),
                ("Image", $"{{{Icon(n)}}}"),
                ("Foreground", $"{{{Foreground(n)}}}"),
                ("Quantity", $"{{{Quantity(n)}}}"),
                ("Visible", $"{{{Icon(n)}}}"), // an empty image name draws the "not found" icon
            ]);
        }

        return template with
        {
            Elements =
            [
                QuickTemplate.Element(
                    ("Type", "Frame"),
                    ("Width", "100"),
                    ("Height", "100"),
                    ("Stroke Width", "0"),
                    ("Background Color", $"{{{Background}}}")
                ),
                .. net.Faces.SelectMany(FaceElements),
            ],
        };
    }

    /// <summary>
    /// The id of the one row a Quick die draws.
    /// </summary>
    public static SnowTag NetRow => SnowTag.Empty;

    /// <summary>
    /// What a Quick die with <paramref name="sideCount"/> draws.
    /// </summary>
    public static Drawing Drawing(DieParameters die, QuickSide side, int sideCount) =>
        new(
            Template(sideCount),
            new Dictionary<SnowTag, IReadOnlyDictionary<string, string>>
            {
                [NetRow] = Values(die.Color, side, sideCount),
            }
        );

    /// <summary>
    /// What draws a die of <paramref name="color"/> with <paramref name="sides"/> sides. Its faces
    /// show the values of <paramref name="side"/>'s suits as one sequence, fitted to the sides.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Values(Color color, QuickSide side, int sides)
    {
        SequenceExpressions.TryParseStrings(side.Expressions(), out var faces, sides);
        var data = new Dictionary<string, string> { [Background] = color.ToHtml() };
        for (int n = 0; n < faces.Length; n++)
        {
            var suit = side.Suits[faces[n].Input];
            var value = faces[n].Value;
            bool hasIcon = suit.Icon != "";
            int icons = QuickTemplate.Icons(value);
            data[Text(n)] = hasIcon ? "" : value;
            data[Icon(n)] = hasIcon && icons > 0 ? suit.Icon : "";
            data[Quantity(n)] = icons.ToString(CultureInfo.InvariantCulture);
            data[Foreground(n)] = suit.Foreground.ToHtml();
        }
        return data;
    }
}
