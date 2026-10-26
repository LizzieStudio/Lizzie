using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Lizzie;

/// <summary>
/// The faces drawn from one template and dataset, or cut from one image, held together.
/// </summary>
/// <remarks>
/// You have to manually call <c>Acquire</c> once per face, and <see cref="Release"/> when the face is done with it.
/// A face keeps its picture for as long as its set is held, so paging back to it shows it at once.
/// </remarks>
public sealed class FaceSet
{
    private static readonly Dictionary<string, FaceSet> Sets = new();

    private readonly string _key;
    private readonly CancellationTokenSource _cancel = new();
    private int _holders;

    // Draws the faces of a set made from atoms.
    private FaceDrawing _drawing;

    // The image whose frames a set made from an image shows, and the faces cut so far.
    private ImageAsset _asset;
    private readonly Dictionary<int, Face> _frames = new();

    private FaceSet(string key) => _key = key;

    /// <summary>
    /// Holds the drawn set under <paramref name="key"/>, creating it if it hasn't been already.
    /// Faces come from <see cref="Face(SnowTag)"/>.
    /// </summary>
    /// <param name="source">Reads what the faces show. A face redraws when what it read changes.</param>
    public static FaceSet Acquire(
        string key,
        TextureFactory factory,
        Func<IAtomReader, Drawing> source
    ) =>
        Hold(
            key,
            () =>
            {
                var set = new FaceSet(key);
                set._drawing = new FaceDrawing
                {
                    FaceSet = set,
                    Factory = factory,
                    Source = source,
                    Token = set._cancel.Token,
                };
                factory.Drawings.AddChild(set._drawing);
                return set;
            }
        );

    /// <summary>
    /// Holds the set of <paramref name="asset"/>'s frames, creating it if it hasn't been already.
    /// Faces come from <see cref="Frame"/>.
    /// </summary>
    public static FaceSet Acquire(ImageAsset asset)
    {
        var key = $"asset:{asset.CloudPath}:{asset.Layout}";
        return Hold(key, () => new FaceSet(key) { _asset = asset });
    }

    private static FaceSet Hold(string key, Func<FaceSet> create)
    {
        if (!Sets.TryGetValue(key, out var set))
            Sets[key] = set = create();
        set._holders++;
        return set;
    }

    /// <summary>
    /// The face of a drawn set that shows <paramref name="row"/>.
    /// </summary>
    public Face Face(SnowTag row) => _drawing.Face(row);

    /// <summary>
    /// The face of an image's set that shows the 0-based <paramref name="frame"/>.
    /// </summary>
    public Face Frame(int frame)
    {
        if (!_frames.TryGetValue(frame, out var face))
        {
            _frames[frame] = face = new Face(this);
            Cut(face, frame);
        }
        return face;
    }

    private async void Cut(Face face, int frame)
    {
        var image = await AssetImageCache.Instance.FetchImageAsync(_asset);
        if (_cancel.IsCancellationRequested)
            return;
        if (image == null)
        {
            await face.Show(null);
            // Faces made after this try again, say once a failed download can succeed.
            if (Sets.GetValueOrDefault(_key) == this)
                Sets.Remove(_key);
            return;
        }

        var layout = _asset.Layout;
        var size = new Vector2I(image.GetWidth() / layout.Columns, image.GetHeight() / layout.Rows);
        var cell = await Task.Run(() =>
            image.GetRegion(
                new Rect2I(
                    new Vector2I(frame % layout.Columns, frame / layout.Columns) * size,
                    size
                )
            )
        );
        await face.Show(cell);
    }

    /// <summary>
    /// Lets go of a set held with <c>Acquire</c>.
    /// The last holder to let go drops it and cancels its drawing.
    /// </summary>
    public void Release()
    {
        if (--_holders > 0)
            return;
        _cancel.Cancel();
        if (GodotObject.IsInstanceValid(_drawing))
            _drawing.QueueFree();
        if (Sets.GetValueOrDefault(_key) == this)
            Sets.Remove(_key);
    }

    /// <summary>
    /// Clears every set. Faces still holding one keep it.
    /// </summary>
    public static void Clear() => Sets.Clear();
}
