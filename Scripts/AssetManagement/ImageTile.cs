using System;
using Godot;
using Lizzie.AssetManagement;

namespace Lizzie;

public partial class ImageTile : MarginContainer
{
    private Label _imageName;
    private TextureRect _thumbnail;

    private SnowTag _imageRef;

    /// <summary>The asset this tile shows.</summary>
    public SnowTag ImageRef
    {
        get => _imageRef;
        set
        {
            _imageRef = value;
            Atoms.R.QueueSync(this);
        }
    }

    public event Action<SnowTag> Clicked;

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _imageName = GetNode<Label>("%ImageName");
        _thumbnail = GetNode<TextureRect>("%Thumbnail");

        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Clicked?.Invoke(ImageRef);
        }
    }

    private async void Sync(IWatchReader R)
    {
        var asset = R.Get(ImageAsset.ById, ImageRef);
        if (asset == null)
            return;

        _imageName.Text = asset.Name;
        var image = await AssetImageCache.Instance.FetchImageAsync(asset);
        // The tile may have been removed or retargeted while the image loaded.
        if (!IsInstanceValid(this) || asset.Id != ImageRef)
            return;
        if (image != null)
            _thumbnail.Texture = ImageTexture.CreateFromImage(image);
    }
}
