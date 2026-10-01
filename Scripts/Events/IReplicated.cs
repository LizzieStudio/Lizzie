using System.Collections.Generic;

/// <summary>
/// Replicated in multiplayer on every update.
/// </summary>
public interface IReplicated
{
    SnowTag Id { get; }

    /// <summary>The id of the event that last wrote this definition.</summary>
    SnowportId LastUpdateId { get; }

    /// <summary>Reversible soft-delete flag.</summary>
    bool Deleted { get; }

    /// <summary>
    /// Returns a copy with a replaced id and lastUpdateId
    /// </summary>
    IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId);

    /// <summary>
    /// A cache key that changes whenever the definition is updated.
    /// </summary>
    public string SheetKey();

    /// <summary>
    /// What's directly inside this record, like a deck's cards or a DataRow's cells.
    /// These are followed recursively, so only list direct contents.
    /// </summary>
    IEnumerable<Target> Contents(IRecordReader R);

    /// <summary>
    /// What this record is directly inside, like a card's deck or a cells's <see cref="DataRow"/> and <see cref="ColumnTarget"/>.
    /// These are followed recursively, so only list direct containers.
    /// </summary>
    IEnumerable<Target> Containers(IRecordReader R);

    /// <summary>
    /// What this record refers to, like a component's prototype.
    /// Not followed further.
    /// </summary>
    IEnumerable<Target> Referenced(IRecordReader R);
}

public static class ReplicatedExtensions
{
    /// <summary>
    /// A cache key that changes whenever any definition is updated.
    /// </summary>
    public static string SheetKey(this IEnumerable<IReplicated> items)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var item in items)
            sb.Append(item.SheetKey());
        return sb.ToString();
    }
}
