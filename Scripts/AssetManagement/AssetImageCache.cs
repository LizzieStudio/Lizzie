using System.Collections.Generic;
using Godot;
using Lizzie.AssetManagement;

/// <summary>
/// Holds lazy-loaded image data for assets, keyed by the images cloud-path.
/// </summary>
public class AssetImageCache
{
    private static AssetImageCache _instance;

    public static AssetImageCache Instance
    {
        get
        {
            if (_instance == null)
                _instance = new AssetImageCache();
            return _instance;
        }
    }

    private readonly Dictionary<string, Image> _images = new();

    /// <summary>Whether the asset's image has been downloaded and cached.</summary>
    public bool IsDownloaded(ImageAsset asset) =>
        asset != null && _images.ContainsKey(asset.CloudPath);

    /// <summary>The cached image for the asset, or null if it has not been downloaded.</summary>
    public Image GetImage(ImageAsset asset)
    {
        if (asset == null)
            return null;
        return _images.TryGetValue(asset.CloudPath, out var image) ? image : null;
    }

    /// <summary>Stores the downloaded image for the asset.</summary>
    public void Store(ImageAsset asset, Image image)
    {
        if (asset == null || image == null)
            return;
        _images[asset.CloudPath] = image;
    }

    public void Clear() => _images.Clear();
}
