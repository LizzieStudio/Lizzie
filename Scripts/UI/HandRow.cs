using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Shows a player's hand of cards or tokens.
/// </summary>
public partial class HandRow : HBoxContainer
{
    private int _seat = -1;

    /// <summary>
    /// The seat whose hand this row shows. Observer seats show no cards.
    /// </summary>
    public int Seat
    {
        get => _seat;
        set
        {
            if (_seat == value)
                return;
            _seat = value;
            RecordService.Instance.QueueSync(this);
        }
    }

    /// <summary>
    /// Whether the cards show their backs rather than their faces.
    /// </summary>
    public bool Back { get; init; }

    private readonly Dictionary<SnowTag, HandCard> _cards = new();

    public override void _EnterTree()
    {
        RecordService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        var hand = PlayerHandService.GetHand(R, Seat);
        var ids = hand.Select(s => s.Id).ToHashSet();

        foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToList())
            RemoveCard(id);

        for (int i = 0; i < hand.Count; i++)
        {
            if (!_cards.TryGetValue(hand[i].Id, out var card))
                card = AddCard(hand[i].Id);
            MoveChild(card, i);
        }
    }

    private HandCard AddCard(SnowTag id)
    {
        var card = new HandCard();
        AddChild(card);
        card.Reference = id;
        card.Back = Back;
        card.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
        card.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
        _cards[id] = card;
        return card;
    }

    private void RemoveCard(SnowTag id)
    {
        if (!_cards.Remove(id, out var card))
            return;
        RemoveChild(card);
        card.QueueFree();
    }
}
