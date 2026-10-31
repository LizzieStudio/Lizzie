using System;
using System.Threading;
using Godot;

namespace Lizzie;

/// <summary>
/// A hybrid timestamp and logic clock.
/// </summary>
public class Snowport
{
    private static Snowport _clock;

    /// <summary>
    /// The local player's clock, which restarts with each new timeline.
    /// </summary>
    public static Snowport Clock
    {
        get => _clock ??= new Snowport();
        set => _clock = value;
    }

    private readonly Func<long> _ticksMsec;

    // Tests sometimes use parallel threads.
    private readonly Lock _lock = new();

    // start at 1 since 0 is used to mean Empty
    private long _useLogicClock = 1;
    private long _localOffsetMsec = 0;
    private long _globalOffsetMsec = 0;

    // A counter for SnowTag identities.
    private int _tagCounter = 1;

    /// <summary>
    /// The low bits of <see cref="SnowStamp.LogicClock"/> that increment for ids made in the same millisecond.
    /// </summary>
    public const int TickBits = 6;

    public Snowport(Func<long> ticksMsec = null)
    {
        _ticksMsec = ticksMsec ?? (() => (long)Time.GetTicksMsec());
        _localOffsetMsec = _ticksMsec();
    }

    /// <summary>The project's shared time, in milliseconds.</summary>
    private long GameTimeMsec => _ticksMsec() - _localOffsetMsec + _globalOffsetMsec;

    /// <summary>
    /// The milliseconds since <paramref name="id"/> was created. Negative when it's in the future.
    /// </summary>
    public long MsecSince(SnowStamp id) => GameTimeMsec - (id.LogicClock >> TickBits);

    /// <summary>
    /// Create a new SnowStamp. Elsewhere, use <see cref="SnowStamp.Create"/>, which calls this on <see cref="Clock"/>.
    /// </summary>
    public SnowStamp Create() => Mint(Source.Me);

    /// <summary>
    /// The clock behind <see cref="SnowStamp.CreateAdmin"/>.
    /// </summary>
    public SnowStamp CreateAdmin()
    {
        if (!Source.Host.IsMe())
            throw new Exception("Only the host can make admin events");
        return Mint(Source.Admin);
    }

    private SnowStamp Mint(Source from)
    {
        lock (_lock)
        {
            // If the game time has advanced, update the hybrid clock to match.
            _useLogicClock = Math.Max(GameTimeMsec << TickBits, _useLogicClock);
            return new SnowStamp(_useLogicClock++, from);
        }
    }

    /// <summary>
    /// Create a new <see cref="SnowTag"/>. Elsewhere, use <see cref="SnowTag.Create"/>, which calls this on <see cref="Clock"/>.
    /// </summary>
    public SnowTag CreateTag()
    {
        lock (_lock)
            return new(Source.Me, _tagCounter++);
    }

    /// <summary>
    /// Advance the tag counter past <paramref name="tag"/> when it's the local player's.
    /// </summary>
    public void ObserveTag(SnowTag tag)
    {
        lock (_lock)
            if (tag.Source.IsMe() && tag.Counter >= _tagCounter)
                _tagCounter = tag.Counter + 1;
    }

    /// <summary>
    /// Advance the hybrid clock past <paramref name="id"/> so the next minted id is greater.
    /// </summary>
    /// <param name="id">The id to consume.</param>
    public void Process(SnowStamp id)
    {
        lock (_lock)
        {
            // If the ID's time appears to be in the future,
            // adjust the local time to match it.
            var gameTime = GameTimeMsec;
            var otherClock = id.LogicClock >> TickBits;
            if (otherClock > gameTime)
                _globalOffsetMsec += otherClock - gameTime;

            // Make sure the next used logic clock is greater than the ID.
            _useLogicClock = Math.Max(id.LogicClock + 1, _useLogicClock);
        }
    }
}

/// <summary>
/// The id of a synchronized table event.
/// <para>
/// Stores a timestamp with a guarenteed order and which source made it.
/// </para>
/// </summary>
/// <remarks>Laid out as <c>[ LogicClock:47 | Source:6 ]</c>.</remarks>
public readonly record struct SnowStamp : IComparable<SnowStamp>, IFormattable
{
    private readonly long ID;

    public static readonly SnowStamp Empty = new(0);

    /// <summary>
    /// Create a new SnowStamp from the local player's clock.
    /// </summary>
    public static SnowStamp Create() => Snowport.Clock.Create();

    /// <summary>
    /// Create a new SnowStamp for an admin event.
    /// Only the host or a solo player can make them.
    /// </summary>
    public static SnowStamp CreateAdmin() => Snowport.Clock.CreateAdmin();

    public SnowStamp(long id)
    {
        ID = id;
    }

    public SnowStamp(long logicClock, Source source)
        : this((logicClock << 6) | source.Value) { }

    /// <summary>
    /// The hybrid clock: the game time in milliseconds then a <see cref="Snowport.TickBits"/> bit increment for events in the same millisecond.
    /// </summary>
    public long LogicClock => (ID >> 6) & 0x7FFFFFFFFFFFL;

    /// <summary>
    /// The source that made this SnowStamp.
    /// </summary>
    public Source Source => new((int)(ID & 0x3F));

    public int CompareTo(SnowStamp other) => ID.CompareTo(other.ID);

    public static bool operator <(SnowStamp left, SnowStamp right) => left.ID < right.ID;

    public static bool operator >(SnowStamp left, SnowStamp right) => left.ID > right.ID;

    public static bool operator <=(SnowStamp left, SnowStamp right) => left.ID <= right.ID;

    public static bool operator >=(SnowStamp left, SnowStamp right) => left.ID >= right.ID;

    public static implicit operator long(SnowStamp id) => id.ID;

    public override string ToString() => ID.ToString();

    public string ToString(string format, IFormatProvider provider) =>
        ID.ToString(format, provider);

    public static SnowStamp Parse(string s) => new(long.Parse(s));

    public static bool TryParse(string s, out SnowStamp id)
    {
        if (long.TryParse(s, out var value))
        {
            id = new SnowStamp(value);
            return true;
        }
        id = Empty;
        return false;
    }
}

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

    /// <summary>
    /// Create a new <see cref="SnowTag"/> from the local player's clock.
    /// </summary>
    public static SnowTag Create() => Snowport.Clock.CreateTag();

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
