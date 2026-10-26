using System;
using Godot;

namespace Lizzie;

/// <summary>
/// The Id for an atom of type <typeparamref name="T"/>.
/// </summary>
public readonly record struct Ref<T>(SnowTag Tag) : IAtomRef<T>, IRef, IComparable<Ref<T>>
    where T : Atom
{
    /// <summary>
    /// Refers to nothing.
    /// </summary>
    public static readonly Ref<T> Empty = default;

    static Ref()
    {
        if (typeof(StagedAtom).IsAssignableFrom(typeof(T)))
            throw new InvalidOperationException(
                $"Ref<{typeof(T).Name}> can't refer to a staged atom, since a tag alone doesn't find one. "
                    + $"Use a StageKey<{typeof(T).Name}>, which adds the stage."
            );
    }

    public static Ref<T> Create() => new(SnowTag.Create());

    public bool IsEmpty => Tag == SnowTag.Empty;

    public static implicit operator Variant(Ref<T> atom) => atom.Tag;

    public static explicit operator Ref<T>(Variant v) => new((SnowTag)v);

    public int CompareTo(Ref<T> other) => Tag.CompareTo(other.Tag);

    public override string ToString() => Tag.ToString();
}

/// <summary>
/// A <see cref="Ref{T}"/> for code that doesn't know <c>T</c>.
/// </summary>
internal interface IRef
{
    SnowTag Tag { get; }
}
