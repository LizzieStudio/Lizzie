using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <para>Shows a player's hand of cards or tokens.</para>
///
/// <inheritdoc cref="HandLayout"/>
/// </summary>
public partial class HandRow : Control
{
    /// <summary>
    /// The scene each card in the row is made from.
    /// </summary>
    [Export]
    private PackedScene _cardScene;

    /// <summary>
    /// The space between cards that fit.
    /// </summary>
    [Export(PropertyHint.Range, "0,40,1,suffix:px")]
    public float Separation { get; set; } = 6;

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
            AtomService.Instance.QueueSync(this);
        }
    }

    [Export]
    public bool AlwaysShowBacks { get; set; }

    private readonly Dictionary<SnowTag, HandCard> _cards = new();

    private readonly List<HandCard> _order = new();

    private int _hovered = -1;

    private int _gapAt = -1;
    private float _gapWidth;

    public override void _EnterTree()
    {
        AtomService.Instance.Watch(this, Sync);
    }

    /// <summary>
    /// The row under the mouse, or null.
    /// </summary>
    public static HandRow Under(Viewport viewport) => viewport.GuiGetHoveredControl() as HandRow;

    /// <summary>
    /// The card under the mouse, or null.
    /// </summary>
    public SnowTag? HoveredCard() =>
        Under(GetViewport()) == this ? CardAt(GetLocalMousePosition().X)?.Reference : null;

    // The card at x, found without the spread or the gap, which would move the cards out from under the pointer.
    private HandCard CardAt(float x) =>
        Layout(-1, -1).CardAt(x) is var i and >= 0 ? _order[i] : null;

    // Picks up the card where it was pressed.
    public override Variant _GetDragData(Vector2 at) =>
        CardAt(at.X) is { } card
            ? ProjectService.Instance.GameObjects.GrabFromHand(
                this,
                card.Reference,
                GetGlobalTransformWithCanvas() * at
            )
            : default;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton button && GameObjects.IsSelectingPress(button))
        {
            ProjectService.Instance.GameObjects.Press(CardAt(button.Position.X)?.Reference);
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
            return;

        var mouse = GetLocalMousePosition();
        bool hovered = Under(GetViewport()) == this;
        // The widths of the cards being dragged, which the row opens a gap for, side by side.
        List<float> held = GetViewport().GuiGetDragData().As<GodotObject>() is HeldDrag drag
            ? drag.Cards(AtomService.Instance)
                .Select(s =>
                    CardSize(
                        TableSize(
                            AtomService.Instance.Get(Prototype.ById, s.PrototypeRef)?.Parameters
                                as PrintedParameters
                        ),
                        HandCard.ScreenRotation(s.Rotation.Y)
                    ).Width
                )
                .ToList()
            : [];

        // Found without the spread or the gap, like CardAt.
        var plain = Layout(-1, -1);
        _hovered = hovered && !GetViewport().GuiIsDragging() ? plain.CardAt(mouse.X) : -1;
        _gapAt = hovered && held.Count > 0 ? plain.InsertIndexAt(mouse.X) : -1;
        _gapWidth = held.Sum() + Separation * Math.Max(held.Count - 1, 0);

        // The spread and the gap follow the mouse, so the cards are placed every frame.
        Place();
    }

    // Held cards drop in where they're let go.
    public override bool _CanDropData(Vector2 at, Variant data) =>
        data.As<GodotObject>() is HeldDrag held && held.Cards(AtomService.Instance).Any();

    public override void _DropData(Vector2 at, Variant data) =>
        ProjectService.Instance.GameObjects.DropInHand(
            (HeldDrag)data.AsGodotObject(),
            Seat,
            InsertIndexAt(at)
        );

    private void Sync(IWatchReader R)
    {
        var hand = PlayerHandService.GetHand(R, Seat);
        var ids = hand.Select(s => s.Id).ToHashSet();

        foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToList())
            RemoveCard(id);

        _order.Clear();
        for (int i = 0; i < hand.Count; i++)
        {
            if (!_cards.TryGetValue(hand[i].Id, out var card))
                card = AddCard(hand[i].Id);
            // Later cards draw over earlier ones.
            MoveChild(card, i);
            _order.Add(card);
        }
    }

    private HandCard AddCard(SnowTag id)
    {
        var card = _cardScene.Instantiate<HandCard>();
        AddChild(card);
        card.Reference = id;
        card.AlwaysShowBack = AlwaysShowBacks;
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

    /// <summary>
    /// How many pixels a centimeter on the table takes in the row, so cards keep their sizes relative to each other.
    ///
    /// <para>
    /// The default fits an upright poker card, 8.89 cm tall, to the 188 pixels a hand starts with.
    /// A player can resize the hand to fit bigger cards.
    /// </para>
    /// </summary>
    [Export(PropertyHint.Range, "1,100,0.1,or_greater,suffix:px/cm")]
    public float PixelsPerCm { get; set; } = 21;

    /// <summary>
    /// The size of a card with <paramref name="p"/> on the table, in cm, or a standard card's size.
    /// </summary>
    public static Vector2 TableSize(PrintedParameters p) =>
        p is { Height: > 0 } ? new(p.Width / 10, p.Height / 10) : new(6.35f, 8.89f);

    /// <summary>
    /// The size of a card <paramref name="tableSize"/> on the table in cm, turned <paramref name="rotation"/> on screen,
    /// and the width it takes in the row.
    /// </summary>
    public (Vector2 Size, float Width) CardSize(Vector2 tableSize, float rotation)
    {
        var size = tableSize * PixelsPerCm;
        float sin = Math.Abs(Mathf.Sin(rotation));
        float cos = Math.Abs(Mathf.Cos(rotation));
        return (size, size.X * cos + size.Y * sin);
    }

    private HandLayout Layout(int hovered, int gapAt) =>
        HandLayout.Place(
            _order
                .Select(c => CardSize(c.TableSize, HandCard.ScreenRotation(c.Yaw)).Width)
                .ToList(),
            Size.X,
            Separation,
            hovered,
            gapAt,
            _gapWidth
        );

    private void Place()
    {
        var layout = Layout(_hovered, _gapAt);
        for (int i = 0; i < _order.Count; i++)
        {
            var card = _order[i];
            float rotation = HandCard.ScreenRotation(card.Yaw);
            var (size, width) = CardSize(card.TableSize, rotation);
            card.Size = size;
            card.PivotOffset = size / 2;
            card.Rotation = rotation;
            card.Position = new Vector2(layout.Lefts[i] + width / 2, Size.Y / 2) - size / 2;
        }
    }

    /// <summary>
    /// The middle of the card showing <paramref name="id"/> as a point on the viewport.
    /// </summary>
    public Vector2 MiddleOf(SnowTag id)
    {
        var card = _cards[id];
        return card.GetGlobalTransformWithCanvas() * (card.Size / 2);
    }

    private int InsertIndexAt(Vector2 at) => Layout(-1, -1).InsertIndexAt(at.X);
}
