using Godot;

/// <summary>
/// A card's face or back as the hand draws it, cut to the token's shape.
/// </summary>
public partial class CardFace : TextureRect
{
    private TextureRect _card;
    private readonly FaceLoader _faces;
    private bool _back;

    public CardFace() => _faces = new FaceLoader(ShowFace);

    public override void _Ready() => _card = GetNode<TextureRect>("Card");

    /// <summary>
    /// Shows card <paramref name="s"/>'s back or face, or nothing for null.
    /// </summary>
    public void ShowCard(ComponentState s, bool back, IRecordReader R)
    {
        var prototype = s == null ? null : R.GetIncludingDeleted<Prototype>(s.PrototypeRef);
        var p = prototype?.Parameters as PrintedParameters;
        Texture = p == null ? null : ((TokenShape)p.Shape).Mask();
        ClipChildren = Texture == null ? ClipChildrenMode.Disabled : ClipChildrenMode.Only;
        _card.Visible = p != null;
        _back = back;
        ShowFace();
        if (p == null)
            _faces.Clear();
        else
            _faces.Load(
                p,
                s.Card,
                prototype,
                ProjectService.Instance.GameObjects.TextureFactory,
                R
            );
    }

    private void ShowFace() => _card.Texture = (_back ? _faces.Back : _faces.Front).Texture;

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
            _faces.Drop();
    }
}
