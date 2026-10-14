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
        _card.Texture = card == null ? null : CardTexture(back ? card.Back : card.Front);
    }

    private static AtlasTexture CardTexture(Face face)
    {
        var sheet = face.Sheet.Texture;
        if (sheet == null)
            return null;

        var layout = face.Sheet.Layout;
        int hframes = Math.Max(1, layout.Columns);
        int vframes = Math.Max(1, layout.Rows);
        int frameW = sheet.GetWidth() / hframes;
        int frameH = sheet.GetHeight() / vframes;
        var region = new Rect2(
            face.Frame % hframes * frameW,
            face.Frame / hframes * frameH,
            frameW,
            frameH
        );
        return new AtlasTexture { Atlas = sheet, Region = region };
    }
}
