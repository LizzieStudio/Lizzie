using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Xunit;

namespace Lizzie.Tests;

public class SideParametersTests
{
    private static QuickSide Quick(params string[] values) =>
        new() { Suits = [.. Array.ConvertAll(values, v => new QuickSuit { Values = v })] };

    private static T RoundTrip<T>(T atom)
        where T : Atom =>
        (T)JsonSerializer.Deserialize<Atom>(JsonSerializer.Serialize<Atom>(atom, Options), Options);

    /// <summary>
    /// The game's options, with colors as plain numbers, since its color converter calls the engine.
    /// </summary>
    private static readonly JsonSerializerOptions Options = WithPlainColors();

    private static JsonSerializerOptions WithPlainColors()
    {
        var options = new JsonSerializerOptions(LizzieJson.EventOptions);
        options.Converters.Insert(0, new PlainColorConverter());
        return options;
    }

    private sealed class PlainColorConverter : JsonConverter<Color>
    {
        public override Color Read(
            ref Utf8JsonReader reader,
            Type type,
            JsonSerializerOptions options
        )
        {
            var c = JsonSerializer.Deserialize<float[]>(ref reader);
            return new Color(c[0], c[1], c[2], c[3]);
        }

        public override void Write(Utf8JsonWriter writer, Color c, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new[] { c.R, c.G, c.B, c.A });
    }

    [Fact]
    public void EachSuitMakesACardPerValue()
    {
        var cards = Quick("1-3", "J, Q").Cards();

        var values = string.Join(", ", cards.ConvertAll(c => c.Value));
        Assert.True(values == "1, 2, 3, J, Q", $"The cards are {values}, expected 1, 2, 3, J, Q.");
    }

    [Fact]
    public void ABlankSuitMakesOneBlankCard()
    {
        var cards = Quick(" ").Cards();

        Assert.True(
            cards.Count == 1 && cards[0].Value == "",
            $"A blank suit makes {cards.Count} cards, expected one blank card."
        );
    }

    [Fact]
    public void TheLongerSideSetsTheCardCount()
    {
        var parameters = new PrintedParameters { Front = Quick("1-3"), Back = Quick("Back") };

        Assert.True(
            parameters.CardCount(null) == 3,
            $"A 3-card front and a 1-card back make {parameters.CardCount(null)} cards, expected 3."
        );
    }

    [Fact]
    public void ARepeatedValueIsOneFace()
    {
        var side = new QuickSide
        {
            Suits = [new QuickSuit { Id = new SnowTag(1), Values = "A*3" }],
        };

        var faces = side.Faces(null);

        Assert.True(
            faces.Count == 3 && faces.Distinct().Count() == 1,
            $"A*3 makes the faces {string.Join(", ", faces)}, expected three of the same face."
        );
    }

    [Fact]
    public void ASingleBackRepeatsForEveryFront()
    {
        var parameters = new PrintedParameters
        {
            Front = new QuickSide
            {
                Suits = [new QuickSuit { Id = new SnowTag(1), Values = "1-3" }],
            },
            Back = new QuickSide { Suits = [new QuickSuit { Id = new SnowTag(2), Values = "B" }] },
        };

        var items = parameters.Items(null);

        var back = new QuickFace(new SnowTag(2), "B");
        Assert.True(
            items.Count == 3 && items.All(item => Equals(item.Back, back)),
            $"The cards are {string.Join(", ", items)}, expected 3 cards with the back {back}."
        );
    }

    [Theory]
    [InlineData("1-3", "A-C", true)]
    [InlineData("1-3", "B", true)]
    [InlineData("F", "A-C", true)]
    [InlineData("1-3", "A-B", false)]
    public void SidesFitWhenTheyPairUp(string front, string back, bool fits)
    {
        var parameters = new PrintedParameters { Front = Quick(front), Back = Quick(back) };

        Assert.True(
            parameters.SidesFit(null) == fits,
            $"A front of {front} and a back of {back} {(fits ? "don't fit" : "fit")}, expected the opposite: "
                + "the sides fit when they have as many cards, or one is a single card."
        );
    }

    [Theory]
    [InlineData("1-9", "1, 2*", true)]
    [InlineData("1-*", "1-3", true)]
    [InlineData("1-3", "1-5, 6*", false)]
    public void AnUnboundedSideFillsToTheOtherSide(string front, string back, bool fits)
    {
        var image = new Ref<ImageAsset>(1);
        var R = new ListReader(new ImageAsset { Id = image, Layout = new(3, 3) });
        var parameters = new PrintedParameters
        {
            Front = new ImageSide { ImageRef = image, Frames = front },
            Back = new ImageSide { ImageRef = image, Frames = back },
        };

        Assert.True(
            parameters.SidesFit(R) == fits,
            $"Frames {front} on the front and {back} on the back {(fits ? "don't fit" : "fit")}, expected the opposite: "
                + "an unbounded expression fills to the other side, but bounded ones are never trimmed."
        );
    }

    [Fact]
    public void EachRowMakesAsManyCardsAsItsQuantity()
    {
        var dataset = new Ref<Dataset>(1);
        var quantity = new Ref<DataColumn>(2);
        Ref<DataRow> a = new(3),
            b = new(4),
            c = new(5),
            d = new(6);
        var rank = default(SnowRank);
        DataRow Row(Ref<DataRow> id)
        {
            rank = rank.After(new SnowportId(id.Tag, Snowport.HostSource));
            return new DataRow
            {
                Id = id,
                DatasetRef = dataset,
                Rank = rank,
            };
        }
        var R = new ListReader(
            new DataColumn { Id = quantity, DatasetRef = dataset },
            Row(a),
            Row(b),
            Row(c),
            Row(d),
            new DataCell { Id = new CellId(a, quantity), Value = "2" },
            new DataCell { Id = new CellId(b, quantity), Value = "0" },
            new DataCell { Id = new CellId(c, quantity), Value = "many" }
        );
        var side = new TemplateSide { DatasetRef = dataset, QuantityRef = quantity };

        var rows = string.Join(", ", side.Rows(R));

        var want = string.Join(", ", a, a, c, d);
        Assert.True(
            rows == want,
            $"The cards show rows {rows}, expected {want}: two of a, none of b, and one each of c and d, whose quantities aren't numbers."
        );
    }

    [Fact]
    public void ADeletedRowMakesNoCard()
    {
        var dataset = new Ref<Dataset>(1);
        Ref<DataRow> kept = new(2),
            deleted = new(3);
        var R = new ListReader(
            new DataRow { Id = kept, DatasetRef = dataset },
            new DataRow
            {
                Id = deleted,
                DatasetRef = dataset,
                Deleted = true,
            }
        );

        var rows = new TemplateSide { DatasetRef = dataset }.Rows(R);

        Assert.True(
            rows.SequenceEqual([kept]),
            $"The cards show rows {string.Join(", ", rows)}, expected only {kept}, since {deleted} is deleted."
        );
    }

    [Theory]
    [MemberData(nameof(Items))]
    public void ItemsSurviveSaving(PrototypeItemRef item)
    {
        var variant = new ComponentVariant { Id = new(1), Item = item };

        var back = RoundTrip(variant).Item;

        Assert.True(
            Equals(back, item),
            $"The item changed in saving.\n\nSaved:  {item?.ToString() ?? "none"}\nLoaded: {back?.ToString() ?? "none"}"
        );
    }

    public static TheoryData<PrototypeItemRef> Items =>
        [
            new PrintedItemRef(null, null),
            new PrintedItemRef(new RowFace(new(3)), new RowFace(new(4))),
            new PrintedItemRef(new QuickFace(new SnowTag(3), "A"), new FrameFace(2)),
            new DieItemRef(Ref<DataRow>.Empty),
            new DieItemRef(new(5)),
            null,
        ];

    [Fact]
    public void HiddenTextSurvivesSaving()
    {
        var prototype = new Prototype
        {
            Id = new(1),
            Parameters = new PrintedParameters
            {
                Front = new QuickSide { Suits = [new QuickSuit { ShowText = false }] },
            },
        };

        var back = RoundTrip(prototype);

        var suit = ((QuickSide)((PrintedParameters)back.Parameters).Front).Suits[0];
        Assert.False(suit.ShowText, "A suit with its text hidden loaded with its text shown.");
    }
}
