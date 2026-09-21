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
