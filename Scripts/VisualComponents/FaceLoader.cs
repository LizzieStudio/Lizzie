using System;
using System.Linq;
using System.Threading.Tasks;
using Lizzie.AssetManagement;

/// <summary>
/// Loads the front and back faces of a printed component, for the table and the hand alike.
/// </summary>
/// <remarks>
/// When a bunch of rapid updates happen to a component, it will continue showing a face,
/// but occasionally replace it with a new one. To achieve this, we keep three pairs of faces in a queue.
/// <list type="number">
/// <item>The shown faces, which could be outdated.</item>
/// <item>The faces that are currently loading to replace them, which aren't canceled when a change is queued.</item>
/// <item>The unloaded faces that are next in line to load when 2 is done, but are replaced when a change is queued.</item>
/// </list>
/// This allows the faces to update even when changes are happening rapidly, like with a color picker.
/// </remarks>
/// <param name="shown">Called when <see cref="Front"/> and <see cref="Back"/> change.</param>
public sealed class FaceLoader(Action shown)
{
    private const float CmPerInch = 2.54f;

    /// <summary>
    /// What the front shows.
    /// </summary>
    public Face Front { get; private set; } = Face.Blank;

    /// <summary>
    /// What the back shows.
    /// </summary>
    public Face Back { get; private set; } = Face.Blank;

    private (Face Front, Face Back)? _loading;
    private (Face Front, Face Back)? _next;
    private bool _dropped;

    /// <summary>
    /// Shows <paramref name="item"/> of <paramref name="prototype"/> once its faces load.
    /// </summary>
    public void Load(
        PrintedParameters p,
        PrototypeItemRef item,
        Prototype prototype,
        TextureFactory factory,
        IRecordReader R
    )
    {
        // Each view holds its set once, so a back that shows the front gets its own view.
        Face FrontView() => View(p, p.Front, "f", item, item.FrontRow, prototype, factory, R);
        var front = FrontView();
        var back =
            (
                p.Back == null
                    ? null
                    : View(p, p.Back, "b", item, item.BackRow, prototype, factory, R)
            ) ?? FrontView();
        Queue(front ?? Face.Blank, back ?? Face.Blank);
    }

    /// <summary>
    /// Shows nothing, letting go of the faces shown.
    /// </summary>
    public void Clear() => Queue(Face.Blank, Face.Blank);

    private void Queue(Face front, Face back)
    {
        Release(_next);
        _next = (front, back);
        if (_loading == null)
            LoadNext();
    }

    /// <summary>
    /// Lets go of every face, once the owner is done with them.
    /// </summary>
    public void Drop()
    {
        _dropped = true;
        Release(_next);
        Release(_loading);
        Release((Front, Back));
    }

    /// <summary>
    /// What <paramref name="side"/> shows on <paramref name="item"/>, or null when it shows nothing.
    /// </summary>
    /// <param name="key">The key to use for the texture cache.</param>
    /// <param name="row">The dataset row this side shows, for a template.</param>
    private static Face View(
        PrintedParameters p,
        SideParameters side,
        string key,
        PrototypeItemRef item,
        SnowTag row,
        Prototype prototype,
        TextureFactory factory,
        IRecordReader R
    )
    {
        int n = Math.Max(item.Number, 0);
        switch (side)
        {
            case QuickSide quick:
            {
                var cards = quick.Cards();
                if (cards.Count == 0)
                    return null;
                // Cards that repeat are only drawn once.
                // This isn't exhaustive, but it catches things like A*2
                var distinct = cards.Distinct().ToList();
                var drawing = new Drawing(
                    QuickTemplate.Template with
                    {
                        Width = p.Width / 10f / CmPerInch,
                        Height = p.Height / 10f / CmPerInch,
                    },
                    QuickTemplate.DataSet,
                    distinct
                        .Select(
                            (c, i) =>
                                QuickTemplate.Row(c.Suit, c.Value) with
                                {
                                    Id = new SnowTag(i + 1),
                                }
                        )
                        .ToList()
                );
                return FaceSet
                    .Acquire($"quick:{prototype.SheetKey()}:{key}", factory, _ => drawing)
                    .Face(new SnowTag(distinct.IndexOf(cards[n % cards.Count]) + 1));
            }

            case ImageSide image:
            {
                var asset = R.Get<ImageAsset>(image.Image);
                var values = image.FrameValues(
                    R,
                    ((PrintedParameters)prototype.Parameters).CardCount(R)
                );
                if (
                    asset == null
                    || n >= values.Length
                    || asset.Layout.Frame(values[n]) is not int frame
                )
                    return Face.Missing;
                return FaceSet.Acquire(asset).Frame(frame);
            }

            case TemplateSide templateSide:
            {
                return FaceSet
                    .Acquire(
                        $"tpl:{templateSide.Template}:{templateSide.Dataset}",
                        factory,
                        R => new Drawing(
                            R.Get<Template>(templateSide.Template),
                            R.Get<DataSet>(templateSide.Dataset),
                            R.GetRows(templateSide.Dataset)
                        )
                    )
                    .Face(row);
            }

            default:
                return null;
        }
    }

    private async void LoadNext()
    {
        var (front, back) = _next.Value;
        (_loading, _next) = (_next, null);
        await Task.WhenAll(front.Loaded, back.Loaded);
        if (_dropped)
            return;
        _loading = null;
        Release((Front, Back));
        (Front, Back) = (front, back);
        shown();
        if (_next != null)
            LoadNext();
    }

    private static void Release((Face Front, Face Back)? faces)
    {
        if (faces is var (front, back))
        {
            front.Set?.Release();
            back.Set?.Release();
        }
    }
}
