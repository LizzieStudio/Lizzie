using System;

namespace Lizzie;

/// <summary>
/// A value that a control shows and edits.
///
/// <para>
/// The control reads <see cref="Get"/> in its Watch, so it follows the atoms and <see cref="WatchableValue{T}"/>s it reads.
/// It calls <see cref="Set"/> when the user changes the value, which should store it where <see cref="Get"/> reads it.
/// </para>
/// </summary>
public readonly record struct Binding<T>(Func<IWatchReader, T> Get, Action<T> Set)
{
    public static implicit operator Binding<T>(WatchableValue<T> value) =>
        new(R => R.Get(value), v => value.Value = v);
}
