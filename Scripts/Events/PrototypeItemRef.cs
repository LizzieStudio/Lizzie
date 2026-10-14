using System.Linq;
using System.Text.Json.Serialization;

/// <summary>
/// Which of its prototype's items a component is.
/// </summary>
/// <param name="Number">Which card in the list. Usually the frame or index.</param>
/// <param name="FrontRow">The row of the  DataSet for the front (or the one to use for a die).</param>
/// <param name="BackRow">The row of the  DataSet for the back.</param>
public readonly record struct PrototypeItemRef(int Number, SnowTag FrontRow, SnowTag BackRow)
{
    /// <summary>
    /// The nil value.
    /// </summary>
    public static PrototypeItemRef None => new(-1, SnowTag.Empty, SnowTag.Empty);

    /// <summary>Whether it refers to one of the items, rather than being <see cref="None"/>.</summary>
    [JsonIgnore]
    public bool IsItem => Number >= 0;

    /// <summary>
    /// The first item of a prototype with these <paramref name="parameters"/>,
    /// or <see cref="None"/> when it has no items.
    /// </summary>
    public static PrototypeItemRef First(ComponentParameters parameters, IRecordReader R) =>
        parameters switch
        {
            PrintedParameters printed => printed.Card(0, R),
            DieParameters { Faces: TemplateSide side } => new(
                0,
                R.GetRows(side.Dataset).FirstOrDefault()?.Id ?? SnowTag.Empty,
                SnowTag.Empty
            ),
            _ => None,
        };
}
