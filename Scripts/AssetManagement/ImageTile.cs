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

        if (AssetStore.Instance != null)
            AssetStore.Instance.AssetsChanged += OnAssetsChanged;

        if (_refreshRequired)
        {
            Refresh();
            _refreshRequired = false;
        }
    }

    private void OnAssetsChanged(int[] ids)
    {
        if (_asset == null)
            return;

        foreach (var id in ids)
            if (id == _asset.Id.Value)
            {
                Refresh();
                return;
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
        var image = await ProjectService.Instance.FetchImageAsync(_asset);
        if (image != null)
            _thumbnail.Texture = ImageTexture.CreateFromImage(image);
    }
}
