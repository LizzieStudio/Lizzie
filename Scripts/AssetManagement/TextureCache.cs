using System;
using System.Collections.Generic;
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

    private readonly Dictionary<Guid, Texture2D> _assetCache = new();
    private readonly Dictionary<string, Texture2D> _derivedCache = new();

    private readonly Dictionary<string, List<Action<Texture2D>>> _pending = new();

    public Texture2D GetOrCreateAssetTexture(Asset asset)
    {
        if (asset == null || asset.Image == null)
            return null;

        if (_assetCache.TryGetValue(asset.AssetId, out var cached))
            return cached;

        var tex = BuildCompressedTexture(asset.Image);
        _assetCache[asset.AssetId] = tex;
        return tex;
    }

    public Texture2D GetOrCreateSheetTexture(string sheetKey, Func<Image> builder)
    {
        if (string.IsNullOrEmpty(sheetKey))
            return null;

        if (_derivedCache.TryGetValue(sheetKey, out var cached))
            return cached;

        var image = builder();
        if (image == null)
            return null;

        var tex = BuildCompressedTexture(image);
        _derivedCache[sheetKey] = tex;
        return tex;
    }

    public Texture2D GetOrCreateSingleTexture(string singleKey, Func<Image> builder)
    {
        return GetOrCreateSheetTexture(singleKey, builder);
    }

    public Texture2D TryGetDerived(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        _derivedCache.TryGetValue(key, out var tex);
        return tex;
    }

    public void PutDerived(string key, Texture2D tex)
    {
        if (string.IsNullOrEmpty(key) || tex == null)
            return;
        _derivedCache[key] = tex;
        if (_pending.TryGetValue(key, out var waiters))
        {
            _pending.Remove(key);
            foreach (var w in waiters)
                w?.Invoke(tex);
        }
    }

    public bool RequestDerived(string key, Action<Texture2D> onReady)
    {
        if (string.IsNullOrEmpty(key))
        {
            onReady?.Invoke(null);
            return false;
        }
        if (_derivedCache.TryGetValue(key, out var cached))
        {
            onReady?.Invoke(cached);
            return false;
        }
        if (_pending.TryGetValue(key, out var existing))
        {
            if (onReady != null)
                existing.Add(onReady);
            return false;
        }
        var waiters = new List<Action<Texture2D>>();
        if (onReady != null)
            waiters.Add(onReady);
        _pending[key] = waiters;
        return true;
    }

    public void Invalidate(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        _derivedCache.Remove(key);
    }

    public void InvalidateAsset(Guid assetId)
    {
        _assetCache.Remove(assetId);
    }

    public void Clear()
    {
        _assetCache.Clear();
        _derivedCache.Clear();
        foreach (var waiters in _pending.Values)
        {
            foreach (var w in waiters)
                w?.Invoke(null);
        }
        _pending.Clear();
    }

    public int Count => _assetCache.Count + _derivedCache.Count;

    private static Texture2D BuildCompressedTexture(Image source)
    {
        var img = (Image)source.Duplicate();

        img.GenerateMipmaps();

        img.Compress(Image.CompressMode.S3Tc, Image.CompressSource.Generic);

        return ImageTexture.CreateFromImage(img);
    }
}
