using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

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
    /// The first item of a prototype with these <paramref name="parameters"/>, or null.
    /// </summary>
    public static PrototypeItemRef First(ComponentParameters parameters, IAtomReader R) =>
        parameters switch
        {
            PrintedParameters printed => printed.Item(0, R),
            DieParameters { Faces: TemplateSide side } => new DieItemRef(
                R.GetRows(side.Dataset).FirstOrDefault()?.Id ?? SnowTag.Empty
            ),
            _ => null,
        };
}

/// <summary>
/// One card of a printed prototype.
/// </summary>
/// <param name="Number">Which card in the list. Usually the frame or index.</param>
/// <param name="FrontRow">The row of the DataSet for the front.</param>
/// <param name="BackRow">The row of the DataSet for the back.</param>
[JsonName("PrintedItem")]
public sealed record PrintedItemRef(int Number, SnowTag FrontRow, SnowTag BackRow)
    : PrototypeItemRef
{
    public override IEnumerable<SnowTag> Rows() => [FrontRow, BackRow];
}

/// <summary>
/// A die made from a <see cref="DataRow"/>.
/// </summary>
/// <param name="Row">The row of the DataSet its faces show.</param>
[JsonName("DieItem")]
public sealed record DieItemRef(SnowTag Row) : PrototypeItemRef
{
    public override IEnumerable<SnowTag> Rows() => [Row];
}
