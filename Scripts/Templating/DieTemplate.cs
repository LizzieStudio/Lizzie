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

    private static readonly Column Background = new() { Id = new SnowTag(1), Name = "Background" };

    // Each face has four columns, after the background.
    private static Column FaceColumn(int face, int offset, string name) =>
        new() { Id = new SnowTag(2 + 4 * face + offset), Name = $"{name}{face + 1}" };

    private static Column Text(int face) => FaceColumn(face, 0, "Text");

    private static Column Icon(int face) => FaceColumn(face, 1, "Icon");

    private static Column Quantity(int face) => FaceColumn(face, 2, "Quantity");

    private static Column Foreground(int face) => FaceColumn(face, 3, "Foreground");

    public static DataSet DataSet(int sides) =>
        new()
        {
            Id = SnowTag.Empty,
            Name = "Quick Die",
            Columns =
            [
                Background,
                .. Enumerable
                    .Range(0, sides)
                    .SelectMany(face =>
                        new[] { Text(face), Icon(face), Quantity(face), Foreground(face) }
                    ),
            ],
        };

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
                ("Text", $"{{{Text(n).Name}}}"),
                ("Foreground", $"{{{Foreground(n).Name}}}"),
                ("Autosize", "True"),
            ]);
            yield return QuickTemplate.Element([
                ("Type", "Image"),
                .. Place(triangles ? 1 : IconScale),
                ("Image", $"{{{Icon(n).Name}}}"),
                ("Foreground", $"{{{Foreground(n).Name}}}"),
                ("Quantity", $"{{{Quantity(n).Name}}}"),
                ("Visible", $"{{{Icon(n).Name}}}"), // an empty image name draws the "not found" icon
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
                    ("Background Color", $"{{{Background.Name}}}")
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
        new(Template(sideCount), DataSet(sideCount), [Row(die.Color, side, sideCount)]);

    /// <summary>
    /// The row that draws a die of <paramref name="color"/> with <paramref name="sides"/> sides. Its faces
    /// show the values of <paramref name="side"/>'s suits as one sequence, fitted to the sides.
    /// </summary>
    public static DataRow Row(Color color, QuickSide side, int sides)
    {
        SequenceExpressions.TryParseStrings(side.Expressions(), out var faces, sides);
        var data = ImmutableDictionary.CreateBuilder<SnowTag, string>();
        data[Background.Id] = color.ToHtml();
        for (int n = 0; n < faces.Length; n++)
        {
            var suit = side.Suits[faces[n].Input];
            var value = faces[n].Value;
            bool hasIcon = suit.Icon != "";
            int icons = QuickTemplate.Icons(value);
            data[Text(n).Id] = hasIcon ? "" : value;
            data[Icon(n).Id] = hasIcon && icons > 0 ? suit.Icon : "";
            data[Quantity(n).Id] = icons.ToString(CultureInfo.InvariantCulture);
            data[Foreground(n).Id] = suit.Foreground.ToHtml();
        }
        return new DataRow { Id = NetRow, Data = data.ToImmutable() };
    }
}
