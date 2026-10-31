using System;
using System.Threading;
using Godot;

namespace Lizzie;

/// <summary>
/// A compact, timestamp-free unique ID for an <see cref="Atom"/>.
/// </summary>
/// <remarks>
/// <para>Laid out as <c>[ Source:6 | Counter:26 ]</c>.</para>
/// <para>
/// Atoms should refer to atoms with a SnowTag, never its value in an int or string.
/// The save file uses a form of garbage collection to keep atoms alive.
/// </para>
/// </remarks>
public readonly record struct SnowTag : IComparable<SnowTag>, IFormattable
{
    private readonly int ID;

    public static readonly SnowTag Empty = new(0);

    // Tests sometimes use parallel threads.
    private static readonly Lock Lock = new();

    // the counter of the next tag
    private static int _next = 1;

    /// <summary>
    /// Create a new <see cref="SnowTag"/> for the local player.
    /// </summary>
    public static SnowTag Create()
    {
        lock (Lock)
            return new(Source.Me, _next++);
    }

    /// <summary>
    /// Advance the counter past <paramref name="tag"/> when it's the local player's, so tags made later don't repeat it.
    /// </summary>
    public static void Observe(SnowTag tag)
    {
        lock (Lock)
            if (tag.Source.IsMe() && tag.Counter >= _next)
                _next = tag.Counter + 1;
    }

    /// <summary>
    /// The maximum counter on any one source.
    /// </summary>
    public const int MaxCounter = (1 << 26) - 1;

    public SnowTag(int id)
    {
        ID = id;
    }

    public SnowTag(Source source, int counter)
        : this((source.Value << 26) | CheckCounter(counter)) { }

    private static int CheckCounter(int counter) =>
        counter is >= 0 and <= MaxCounter
            ? counter
            : throw new ArgumentOutOfRangeException(
                nameof(counter),
                counter,
                "This source has run out of SnowTags."
            );

    /// <summary>
    /// The source that minted this tag.
    /// </summary>
    public Source Source => new((ID >> 26) & 0x3F);

    /// <summary>
    /// The ID without the source number.
    /// </summary>
    public int Counter => ID & MaxCounter;

    public int CompareTo(SnowTag other) => ID.CompareTo(other.ID);

    public static implicit operator SnowTag(int x) => new(x);

    public static implicit operator int(SnowTag x) => x.ID;

    public static implicit operator Variant(SnowTag tag) => tag.ID;

    public static explicit operator SnowTag(Variant v) => new(v.AsInt32());

    public override string ToString() => ID.ToString();

    public string ToString(string format, IFormatProvider provider) =>
        ID.ToString(format, provider);

    public static SnowTag Parse(string s) => new(int.Parse(s));

    public static bool TryParse(string s, out SnowTag tag)
    {
        if (int.TryParse(s, out var value))
        {
            tag = new SnowTag(value);
            return true;
        }
        tag = Empty;
        return false;
    }
}
