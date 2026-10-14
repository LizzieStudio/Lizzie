using Godot;

/// <summary>
/// Edits an <see cref="ImageSide"/>.
/// </summary>
public partial class ImageSideTab : SideTab
{
    [Export]
    private ImageSelector _image;

    [Export]
    private SequenceEdit _frames;

    private SnowTag _imageRef = SnowTag.Empty;

    public override void _Ready()
    {
        _image.ImageSelected += id =>
        {
            _imageRef = id;
            RaiseChanged();
        };
        _frames.TextChanged += _ => RaiseChanged();
    }

    public override SideParameters Side =>
        new ImageSide { Image = _imageRef, Frames = _frames.Text };

    public override bool TryLoad(SideParameters side)
    {
        if (side is not ImageSide image)
            return false;
        _imageRef = image.Image;
        _image.SetSelectedImage(image.Image);
        _frames.Text = image.Frames;
        return true;
    }
}
