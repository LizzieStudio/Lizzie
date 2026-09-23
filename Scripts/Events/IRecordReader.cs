using System;
using System.Collections.Generic;

/// <summary>
/// Reads project records. Soft-deleted records are never returned.
/// A reader passed to a watched Sync records every read as a dependency.
/// </summary>
public interface IRecordReader
{
    /// <summary>The record, or null if it is missing or deleted.</summary>
    T Get<T>(SnowTag id)
        where T : class, IReplicated;

    /// <summary>The records that exist, in the order of <paramref name="ids"/>.</summary>
    IReadOnlyList<T> Get<T>(IEnumerable<SnowTag> ids)
        where T : class, IReplicated;

    /// <summary>Every record that passes <paramref name="filter"/>, which must only read the record.</summary>
    IReadOnlyList<T> Get<T>(Func<T, bool> filter)
        where T : class, IReplicated;

    /// <summary>Every record, including ones added later.</summary>
    IReadOnlyList<T> Get<T>()
        where T : class, IReplicated;

    /// <summary>The current value of a <see cref="ReplicatedValue{T}"/>.</summary>
    T Value<T>()
        where T : class;
}
