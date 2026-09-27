/// <summary>Commands for decks.</summary>
public static partial class CommandList
{
    public static readonly Command ShuffleDeck = new ComponentCommand
    {
        Id = new("deck.shuffle"),
        Caption = "Shuffle {0}",
        Noun = ("Deck", "Decks"),
        AppliesTo = c => c is VcDeck,
        Action = (cs, _) => Send(VisualCommand.Shuffle, cs),
    };

    public static readonly Command DrawCards = new ComponentCommand
    {
        Id = new("deck.draw"),
        Caption = "Draw Cards",
        AsksQuantity = true,
        NumberKeys = true,
        AppliesTo = c => c is VcDeck,
        Action = (cs, n) => Send(VisualCommand.Draw, cs, n),
    };

    public static readonly Command DealCards = new ComponentCommand
    {
        Id = new("deck.deal"),
        Caption = "Deal Cards",
        AsksQuantity = true,
        AppliesTo = c => c is VcDeck,
        Action = (cs, n) => Send(VisualCommand.Deal, cs, n),
    };
}
