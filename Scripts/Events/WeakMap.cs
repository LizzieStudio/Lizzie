using System;
using System.Collections;
using System.Collections.Immutable;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// An immutable map whose keys don't keep atoms alive in saves, like JavaScript's WeakMap.
///
/// <para>
/// A save writes an entry only while something else reaches its key's SnowTags,
/// so an entry lasts as long as what it describes.
/// This is useful for keeping data about an atom that will disappear when that atom does.
/// A written entry's value keeps what it refers to, like any other saved data.
/// </para>
/// </summary>
/// <remarks>
/// It's weak only in saves. In a session nothing is dropped, so undo can bring a key back with its entry.
/// This does not interact with C# garbage collection.
/// </remarks>
public sealed class WeakMap<TKey, TValue> : IWeakMap
    where TKey : IComparable<TKey>
{
    public static readonly WeakMap<TKey, TValue> Empty = new(
        ImmutableSortedDictionary<TKey, TValue>.Empty
    );

    internal ImmutableSortedDictionary<TKey, TValue> Entries { get; }

    internal WeakMap(ImmutableSortedDictionary<TKey, TValue> entries) => Entries = entries;

    IDictionary IWeakMap.Entries => Entries;

    public TValue this[TKey key] => Entries[key];

    public bool TryGetValue(TKey key, out TValue value) => Entries.TryGetValue(key, out value);

    public TValue GetValueOrDefault(TKey key) => Entries.GetValueOrDefault(key);

    public WeakMap<TKey, TValue> With(TKey key, TValue value) => new(Entries.SetItem(key, value));
}
