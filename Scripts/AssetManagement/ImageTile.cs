using System;
using Godot;
using Lizzie.AssetManagement;

public partial class ImageTile : MarginContainer
{
    private Label _imageName;
    private TextureRect _thumbnail;
    private Asset _asset;

    private bool _refreshRequired;
    private bool _selected;

    public event Action<Asset> Clicked;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _imageName = GetNode<Label>("%ImageName");
        _thumbnail = GetNode<TextureRect>("%Thumbnail");

        MouseFilter = MouseFilterEnum.Stop;
        UpdateSelectedVisual();

        if (_refreshRequired)
        {
            Refresh();
            _refreshRequired = false;
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Clicked?.Invoke(_asset);
        }
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        if (IsNodeReady())
        {
            UpdateSelectedVisual();
        }
    }

    private void UpdateSelectedVisual()
    {
        Modulate = _selected ? new Color(0.6f, 0.8f, 1f) : Colors.White;
    }

    public void SetAsset(Asset asset)
    {
        _asset = asset;

        if (IsNodeReady())
        {
            Refresh();
        }
        else
        {
            _refreshRequired = true;
        }
    }

    public async void Refresh()
    {
        _imageName.Text = _asset.Name;
        var image = await ProjectService.Instance.FetchImageAsync(_asset);
        if (image != null)
            _thumbnail.Texture = ImageTexture.CreateFromImage(image);
    }
}
