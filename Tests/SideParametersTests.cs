using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Xunit;

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
    public void TheFirstCardSurvivesSaving()
    {
        var card = new ComponentState
        {
            Id = new SnowTag(1),
            Item = new PrototypeItemRef(0, SnowTag.Empty, SnowTag.Empty),
        };

        var back = RoundTrip(card);

        Assert.True(back.IsItem, $"Card {card.Item} loaded as {back.Item}, which isn't a card.");
    }

    [Fact]
    public void HiddenTextSurvivesSaving()
    {
        var prototype = new Prototype
        {
            Id = new SnowTag(1),
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
