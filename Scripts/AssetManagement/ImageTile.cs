using System;
using Godot;
using Lizzie.AssetManagement;

public partial class ImageTile : MarginContainer
{
    private Label _imageName;
    private TextureRect _thumbnail;

    private bool _refreshRequired;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _imageName = GetNode<Label>("%ImageName");
        _thumbnail = GetNode<TextureRect>("%Thumbnail");
        EventBus.Instance.Subscribe<AssetChangedEvent>(OnAssetChanged);
        if (_refreshRequired)
        {
            Refresh();
            _refreshRequired = false;
        }
    }

    private void OnAssetChanged(AssetChangedEvent obj)
    {
        if (obj == null || obj.Asset == null || _asset == null)
            return;

        if (obj.Asset.AssetId == _asset.AssetId)
        {
            Refresh();
        }
    }

    private Asset _asset;

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
        await ProjectService.Instance.FetchImageAsync(_asset, OnImageFetched);
    }

    private void OnImageFetched(Asset asset)
    {
        _thumbnail.Texture = ImageTexture.CreateFromImage(_asset.Image);
    }
}
