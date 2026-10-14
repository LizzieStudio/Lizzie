using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

public class TextureCache
{
    private static TextureCache _instance;

    public static TextureCache Instance
    {
        get
        {
            if (_instance == null)
                _instance = new TextureCache();
            return _instance;
        }
    }

    private readonly Dictionary<string, Texture2D> _assetCache = new();

    public async Task<Texture2D> GetOrCreateAssetTexture(ImageAsset asset)
    {
        if (asset == null)
            return null;

        var image = await AssetImageCache.Instance.FetchImageAsync(asset);
        if (image == null)
            return null;

        var key = asset.CloudPath;
        if (_assetCache.TryGetValue(key, out var cached))
            return cached;

        var tex = BuildCompressedTexture(image);
        _assetCache[key] = tex;
        return tex;
    }

    public void Clear()
    {
        _assetCache.Clear();
        Sheet.Clear();
    }

    private static Texture2D BuildCompressedTexture(Image source)
    {
        var img = (Image)source.Duplicate();

        img.GenerateMipmaps();

        img.Compress(Image.CompressMode.S3Tc, Image.CompressSource.Generic);

        return ImageTexture.CreateFromImage(img);
    }
}
