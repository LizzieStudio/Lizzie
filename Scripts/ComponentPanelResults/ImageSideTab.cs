using Godot;

/// <summary>
/// Edits an <see cref="ImageSide"/>.
/// </summary>
public partial class ImageSideTab : SideTab
{
    [Export]
    private ImageSelector _image;

    [Export]
    private LineEdit _frames;

    private SnowTag _imageRef = SnowTag.Empty;

    public override void _Ready()
    {
        _image.ImageSelected += id =>
        {
            _imageRef = id;
            RaiseChanged();
        };
        _frames.TextChanged += _ =>
        {
            MarkFrames();
            RaiseChanged();
        };
    }

    private void MarkFrames() =>
        _frames.MarkValidity(SequenceExpressions.TryParseNumbers(_frames.Text, out _));

    public override SideParameters Side =>
        new ImageSide { Image = _imageRef, Frames = _frames.Text };

    public override bool TryLoad(SideParameters side)
    {
        if (side is not ImageSide image)
            return false;
        _imageRef = image.Image;
        _image.SetSelectedImage(image.Image);
        _frames.Text = image.Frames;
        MarkFrames();
        return true;
    }
}
