using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lizzie.AssetManagement;

namespace Lizzie;

/// <summary>
/// Loads the front and back faces of a printed component, for the view and the hand alike.
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
    /// A die shows its net on the front.
    /// </summary>
    public void Load(
        Prototype prototype,
        PrototypeItemRef item,
        TextureFactory factory,
        IAtomReader R
    )
    {
        var card = item as PrintedItemRef;
        Face View(SideParameters side, string key, SnowTag row) =>
            SideView(side, key, card?.Number ?? 0, row, prototype, factory, R);
        switch (prototype.Parameters)
        {
            case PrintedParameters p:
                // Each view holds its set once, so a back that shows the front gets its own view.
                var frontRow = card?.FrontRow ?? SnowTag.Empty;
                var front = View(p.Front, "f", frontRow);
                var back =
                    (p.Back == null ? null : View(p.Back, "b", card?.BackRow ?? SnowTag.Empty))
                    ?? View(p.Front, "f", frontRow);
                Queue(front ?? Face.Blank, back ?? Face.Blank);
                break;
            case DieParameters p:
                var row = (item as DieItemRef)?.Row ?? SnowTag.Empty;
                Queue(View(p.Faces, "f", row) ?? Face.Blank, Face.Blank);
                break;
        }
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
    /// What <paramref name="side"/> shows on card <paramref name="n"/>, or null when it shows nothing.
    /// </summary>
    /// <param name="key">The key to use for the texture cache.</param>
    /// <param name="row">The dataset row this side shows, for a template.</param>
    private static Face SideView(
        SideParameters side,
        string key,
        int n,
        SnowTag row,
        Prototype prototype,
        TextureFactory factory,
        IAtomReader R
    )
    {
        switch (side)
        {
            case QuickSide quick when prototype.Parameters is PrintedParameters p:
            {
                if (QuickTemplate.CardRow(quick, n) is not SnowTag cardRow)
                    return null;
                return FaceSet
                    .Acquire(
                        $"quick:{R.VersionKey(prototype)}:{key}",
                        factory,
                        _ => QuickTemplate.Drawing(p, quick)
                    )
                    .Face(cardRow);
            }

            case QuickSide quick when prototype.Parameters is DieParameters die:
            {
                int sides = die.Resolve(R).Sides;
                return FaceSet
                    .Acquire(
                        $"quick:{R.VersionKey(prototype)}:{key}",
                        factory,
                        _ => DieTemplate.Drawing(die, quick, sides)
                    )
                    .Face(DieTemplate.NetRow);
            }

            case ImageSide image:
            {
                var asset = R.Get(ImageAsset.ById, image.Image);
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
                            R.Get(Template.ById, templateSide.Template),
                            R.Get(DataSet.ById, templateSide.Dataset) == null
                                ? new Dictionary<SnowTag, IReadOnlyDictionary<string, string>>()
                                : R.GetRows(templateSide.Dataset).ToDictionary(r => r.Id, R.Values)
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
