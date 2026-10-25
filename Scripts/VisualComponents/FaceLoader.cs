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
        Face View(SideParameters side, string key, FaceKey face) =>
            SideView(side, key, face, prototype, factory, R);
        switch (prototype.Parameters)
        {
            case PrintedParameters p:
                var card = item as PrintedItemRef;
                // Each view holds its set once, so a back that shows the front gets its own view.
                var front = View(p.Front, "f", card?.Front);
                var back =
                    (p.Back == null ? null : View(p.Back, "b", card?.Back))
                    ?? View(p.Front, "f", card?.Front);
                Queue(front ?? Face.Blank, back ?? Face.Blank);
                break;
            case DieParameters p:
                var row = (item as DieItemRef)?.RowRef ?? SnowTag.Empty;
                Queue(View(p.Faces, "f", new RowFace(row)) ?? Face.Blank, Face.Blank);
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
    /// What <paramref name="side"/> shows for <paramref name="face"/>, or null when it shows nothing.
    /// </summary>
    /// <param name="key">The key to use for the texture cache.</param>
    private static Face SideView(
        SideParameters side,
        string key,
        FaceKey face,
        Prototype prototype,
        TextureFactory factory,
        IAtomReader R
    )
    {
        switch (side)
        {
            case QuickSide quick when prototype.Parameters is PrintedParameters p:
            {
                if (face is not QuickFace quickFace)
                    return null;
                var row = QuickTemplate.Row(quick, quickFace);
                if (row != SnowTag.Empty)
                    return FaceSet
                        .Acquire(
                            $"quick:{R.VersionKey(prototype)}:{key}",
                            factory,
                            _ => QuickTemplate.Drawing(p, quick.Cards())
                        )
                        .Face(row);
                // A retired card whose value the side no longer has still draws, alone, in a placeholder if its suit is gone.
                var suit =
                    quick.Suits.FirstOrDefault(s => s.Id == quickFace.SuitRef)
                    ?? QuickSuit.Placeholder(quickFace.SuitRef);
                return FaceSet
                    .Acquire(
                        $"quick:{R.VersionKey(prototype)}:{key}:{quickFace.SuitRef}:{quickFace.Value}",
                        factory,
                        _ => QuickTemplate.Drawing(p, [(suit, quickFace.Value)])
                    )
                    .Face(new SnowTag(1));
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
                var asset = R.Get(ImageAsset.ById, image.ImageRef);
                if (
                    face is not FrameFace frameFace
                    || asset?.Layout.Frame(frameFace.Frame) is not int frame
                )
                    return Face.Missing;
                return FaceSet.Acquire(asset).Frame(frame);
            }

            case TemplateSide templateSide:
            {
                var row = (face as RowFace)?.RowRef ?? SnowTag.Empty;
                FaceSet Set(string key, Func<IAtomReader, IEnumerable<DataRow>> rows) =>
                    FaceSet.Acquire(
                        $"tpl:{templateSide.TemplateRef}:{templateSide.DatasetRef}{key}",
                        factory,
                        R => new Drawing(
                            R.Get(Template.ById, templateSide.TemplateRef),
                            rows(R).ToDictionary(r => r.Id, R.Values)
                        )
                    );
                // A card whose row was deleted still draws it, alone.
                if (R.Get(DataRow.ById, row) is { Deleted: true })
                    return Set(
                            $":{row}",
                            R => R.Get(DataRow.ById, row) is { } deleted ? [deleted] : []
                        )
                        .Face(row);
                return Set(
                        "",
                        R =>
                            R.Get(Dataset.ById, templateSide.DatasetRef) == null
                                ? []
                                : R.GetRows(templateSide.DatasetRef)
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
