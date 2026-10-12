using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

/// <summary>
/// Downloads each asset's image once and keeps it, keyed by the image's cloud path.
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

    private readonly Dictionary<string, Task<Image>> _images = new();

    /// <summary>
    /// The asset's image, cached for future calls.
    /// The task resolves to null if the download failed.
    /// Calling this method again will retry the download.
    /// </summary>
    public async Task<Image> FetchImageAsync(ImageAsset asset)
    {
        if (asset == null)
            return null;

        var path = asset.CloudPath;
        if (!_images.TryGetValue(path, out var fetch))
            _images[path] = fetch = ImageDownload.DownloadAsync(path);

        var image = await fetch;
        // After a failed download, another caller may have started a new one.
        if (image == null && _images.GetValueOrDefault(path) == fetch)
            _images.Remove(path);
        return image;
    }

    /// <summary>
    /// The cached image, or null.
    /// </summary>
    public Image GetImage(ImageAsset asset) =>
        asset != null
        && _images.TryGetValue(asset.CloudPath, out var fetch)
        && fetch.IsCompletedSuccessfully
            ? fetch.Result
            : null;
}
