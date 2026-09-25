using System;
using Godot;
using Lizzie.AssetManagement;

public partial class ImageTile : MarginContainer
{
    private Label _imageName;
    private TextureRect _thumbnail;

    private bool _selected;

    private SnowTag _reference;

    /// <summary>The asset this tile shows.</summary>
    public SnowTag Reference
    {
        get => _reference;
        set
        {
            _reference = value;
            ProjectService.Instance.QueueSync(this);
        }
    }

    public event Action<SnowTag> Clicked;

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _imageName = GetNode<Label>("%ImageName");
        _thumbnail = GetNode<TextureRect>("%Thumbnail");

        MouseFilter = MouseFilterEnum.Stop;
        UpdateSelectedVisual();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Clicked?.Invoke(Reference);
        }
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        UpdateSelectedVisual();
    }

    private void UpdateSelectedVisual()
    {
        Modulate = _selected ? new Color(0.6f, 0.8f, 1f) : Colors.White;
    }

    private async void Sync(IRecordReader R)
    {
        var asset = R.Get<Asset>(Reference);
        if (asset == null)
            return;

        _imageName.Text = asset.Name;
        var image = await ProjectService.Instance.FetchImageAsync(asset);
        // The tile may have been removed or retargeted while the image loaded.
        if (!IsInstanceValid(this) || asset.Id != Reference)
            return;
        if (image != null)
            _thumbnail.Texture = ImageTexture.CreateFromImage(image);
    }
}
