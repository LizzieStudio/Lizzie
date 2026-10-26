using System;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Edits an <see cref="ImageSide"/>.
/// </summary>
public partial class ImageSideTab : SideTab
{
    [Export]
    private AtomSelector _image;

    [Export]
    private Button _editImage;

    [Export]
    private SequenceEdit _frames;

    private readonly WatchableValue<ImageSide> _side = new() { Value = new() };

    public override void _Ready()
    {
        _image.List(
            R => R.Get(ImageAsset.All).Select(asset => (asset.Id, asset.Name)),
            new(R => R.Get(_side).ImageRef, id => Edit(s => s with { ImageRef = id }))
        );
        _editImage.Pressed += () =>
            EventBus.Instance.Publish(
                new ShowImageManagerEvent { ImageRef = _side.Value.ImageRef }
            );
        _frames.TextChanged += text => Edit(s => s with { Frames = text });
    }

    private void Edit(Func<ImageSide, ImageSide> edit)
    {
        _side.Value = edit(_side.Value);
        RaiseChanged();
    }

    public override SideParameters Side => _side.Value;

    // The frames field isn't filled from a watch, so typing in it isn't interrupted.
    public override bool TryLoad(SideParameters side)
    {
        if (side is not ImageSide image)
            return false;
        _side.Value = image;
        _frames.Text = image.Frames;
        return true;
    }
}
