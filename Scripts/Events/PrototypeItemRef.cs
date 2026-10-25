using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Which of its prototype's items a component is.
/// </summary>
[JsonPolymorphic]
public abstract record PrototypeItemRef
{
    /// <summary>
    /// The dataset rows it shows, for a template.
    /// </summary>
    public abstract IEnumerable<SnowTag> Rows();

    /// <summary>
    /// Every item of a prototype with these <paramref name="parameters"/>, in order.
    /// Empty for a prototype whose components aren't items.
    /// </summary>
    public static IReadOnlyList<PrototypeItemRef> All(
        ComponentParameters parameters,
        IAtomReader R
    ) =>
        parameters switch
        {
            PrintedParameters printed => printed.Items(R),
            DieParameters { Faces: TemplateSide side } => side.Rows(R)
                .Select(row => new DieItemRef(row))
                .ToList(),
            _ => [],
        };
}

/// <summary>
/// One card of a printed prototype.
/// </summary>
/// <param name="Front">What the front shows, or null for nothing.</param>
/// <param name="Back">What the back shows, or null when the back shows the front.</param>
[JsonName("PrintedItem")]
public sealed record PrintedItemRef(FaceKey Front, FaceKey Back) : PrototypeItemRef
{
    public override IEnumerable<SnowTag> Rows() =>
        new[] { Front, Back }.OfType<RowFace>().Select(face => face.RowRef);
}

/// <summary>
/// A die made from a <see cref="DataRow"/>.
/// </summary>
/// <param name="RowRef">The row of the Dataset its faces show.</param>
[JsonName("DieItem")]
public sealed record DieItemRef(SnowTag RowRef) : PrototypeItemRef
{
    public override IEnumerable<SnowTag> Rows() => [RowRef];
}

/// <summary>
/// What one face of a component shows.
/// </summary>
[JsonPolymorphic]
public abstract record FaceKey;

/// <summary>
/// A dataset row drawn with a template.
/// </summary>
[JsonName("RowFace")]
public sealed record RowFace(SnowTag RowRef) : FaceKey;

/// <summary>
/// A frame of an image, counting from 1.
/// </summary>
[JsonName("FrameFace")]
public sealed record FrameFace(int Frame) : FaceKey;

/// <summary>
/// A value of a <see cref="QuickSuit"/>.
/// </summary>
/// <param name="SuitRef">The suit's <see cref="QuickSuit.Id"/>.</param>
[JsonName("QuickFace")]
public sealed record QuickFace(SnowTag SuitRef, string Value) : FaceKey;
