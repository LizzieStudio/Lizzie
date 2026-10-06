using System;
using Godot;

/// <summary>
/// Shows one card in a hand, drawn from its token's face or back texture.
/// </summary>
public partial class HandCard : TextureRect
{
    private SnowTag _reference;

    /// <summary>
    /// The card this row shows.
    /// </summary>
    public SnowTag Reference
    {
        get => _reference;
        set
        {
            _reference = value;
            RecordService.Instance.QueueSync(this);
        }
    }

    /// <summary>
    /// Whether to show the card's back rather than its face.
    /// </summary>
    public bool Back { get; set; }

    // The node whose textures are shown, which the table replaces when the card's prototype or row changes.
    private VcToken _card;

    public override void _EnterTree()
    {
        RecordService.Instance.Watch(this, Sync);
    }

    public override void _ExitTree()
    {
        SetCard(null);
    }

    private void Sync(IRecordReader R)
    {
        var state = R.Get<ComponentState>(Reference);
        if (state != null)
            R.GetIncludingDeleted<Prototype>(state.PrototypeRef);

        SetCard(ProjectService.Instance.GameObjects.GetComponent(Reference) as VcToken);
        Texture = _card == null ? null : CardTexture(_card, Back);
    }

    private void SetCard(VcToken card)
    {
        if (card == _card)
            return;
        if (IsInstanceValid(_card))
            _card.TexturesChanged -= OnTexturesChanged;
        _card = card;
        if (_card != null)
            _card.TexturesChanged += OnTexturesChanged;
    }

    private void OnTexturesChanged() => RecordService.Instance.QueueSync(this);

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            var go = ProjectService.Instance.GameObjects;
            go.StartDraw(go.BuildDraw([Reference]));
            AcceptEvent();
        }
    }

    /// <summary>
    /// The card's cropped texture from the sheet, or null before it has one.
    /// </summary>
    public static AtlasTexture CardTexture(VcToken card, bool back)
    {
        var sheet = back ? card.BackTexture : card.FaceTexture;

        if (sheet == null || sheet.GetWidth() == 0 || sheet.GetHeight() == 0)
            return null;

        int hframes = Math.Max(1, back ? card.BackHframes : card.FaceHframes);
        int vframes = Math.Max(1, back ? card.BackVframes : card.FaceVframes);
        int frame = back ? card.BackFrame : card.FaceFrame;
        int frameW = sheet.GetWidth() / hframes;
        int frameH = sheet.GetHeight() / vframes;
        var region = new Rect2(frame % hframes * frameW, frame / hframes * frameH, frameW, frameH);
        return new AtlasTexture { Atlas = sheet, Region = region };
    }
}
