using System;
using Godot;

/// <summary>
/// A card's face or back as the hand draws it, cut to the token's shape.
/// </summary>
public partial class CardFace : TextureRect
{
    private TextureRect _card;

    public override void _Ready() => _card = GetNode<TextureRect>("Card");

    /// <summary>
    /// Shows <paramref name="card"/>'s back or face, or nothing for null.
    /// </summary>
    public void ShowCard(VcToken card, bool back)
    {
        Texture = card?.Shape.Mask();
        ClipChildren = Texture == null ? ClipChildrenMode.Disabled : ClipChildrenMode.Only;
        _card.Texture = card == null ? null : CardTexture(card, back);
    }

    private static AtlasTexture CardTexture(VcToken card, bool back)
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
