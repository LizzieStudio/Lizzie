using System;
using Godot;

/// <summary>
/// A hybrid timestamp and logic clock with a source ID to make it unique to the table.
/// </summary>
public class Snowport
{
    /// <summary>
    /// The source of admin events, which belong to the table rather than a player.
    /// The host or a solo player writes them, and no one can undo them.
    /// </summary>
    public const byte AdminSource = 0;

    /// <summary>
    /// The host's source, which a solo player uses too. Joining players get the next ones.
    /// </summary>
    public const byte HostSource = 1;

    // Different source codes will be used when joining games.
    public static Snowport Clock = new Snowport(HostSource);

    // start at 1 since 0 is used to mean Empty
    private long _useLogicClock = 1;
    private long _localOffsetMsec = 0;
    private long _globalOffsetMsec = 0;

    // A counter for SnowTag identities.
    private int _tagCounter = 1;

    public readonly byte source;

    /// <summary>How many sources there are, 0 to 63, since ids give the source 6 bits.</summary>
    public const int SourceCount = 64;

    public Snowport(byte source)
    {
        if (source >= SourceCount)
            throw new ArgumentOutOfRangeException(nameof(source), source, "A source takes 6 bits.");
        _localOffsetMsec = (long)Time.GetTicksMsec();
        this.source = source;
    }

    /// <summary>
    /// Returns a clock under a new source that keeps the game time, hybrid clock, and tag counter.
    /// Use this instead of <c>new Snowport(source)</c> when the local player switches source
    /// while keeping the current table (hosting or disconnecting).
    /// </summary>
    public Snowport WithSource(byte newSource)
    {
        return new Snowport(newSource)
        {
            _localOffsetMsec = _localOffsetMsec,
            _globalOffsetMsec = _globalOffsetMsec,
            _useLogicClock = _useLogicClock,
            _tagCounter = _tagCounter,
        };
    }

    /// <summary>The table's shared time, in milliseconds.</summary>
    private long GameTimeMsec => (long)Time.GetTicksMsec() - _localOffsetMsec + _globalOffsetMsec;

    /// <summary>
    /// The milliseconds since <paramref name="id"/> was created. Negative when it's in the future.
    /// </summary>
    public long MsecSince(SnowportId id) => GameTimeMsec - (id.logicClock >> 8);

    /// <summary>
    /// Create a new SnowportId.
    /// </summary>
    /// <returns></returns>
    public SnowportId Create() => Mint(source);

    /// <summary>
    /// Create a new SnowportId for an admin event.
    /// Only the host or a solo player can make them.
    /// </summary>
    public SnowportId CreateAdmin()
    {
        if (source != HostSource)
            throw new Exception("Only the host can make admin events");
        return Mint(AdminSource);
    }

    private SnowportId Mint(byte from)
    {
        // If the game time has advanced, update the hybrid clock to match.
        _useLogicClock = Math.Max(GameTimeMsec << 8, _useLogicClock);
        return new SnowportId(_useLogicClock++, from);
    }

    /// <summary>
    /// Create a new <see cref="SnowTag"/>.
    /// </summary>
    public SnowTag CreateTag() => new(source, _tagCounter++);

    /// <summary>
    /// Advance the tag counter past <paramref name="tag"/> when it shares our source.
    /// </summary>
    public void ObserveTag(SnowTag tag)
    {
        if (tag.source == source && tag.counter >= _tagCounter)
            _tagCounter = tag.counter + 1;
    }

    /// <summary>
    /// Advance the hybrid clock past <paramref name="id"/> so the next minted id is greater.
    /// </summary>
    /// <param name="id">The id to consume.</param>
    public void Process(SnowportId id)
    {
        // If the ID's time appears to be in the future,
        // adjust the local time to match it.
        var gameTime = GameTimeMsec;
        var otherClock = id.logicClock >> 8;
        if (otherClock > gameTime)
            _globalOffsetMsec += otherClock - gameTime;

        // Make sure the next used logic clock is greater than the ID.
        _useLogicClock = Math.Max(id.logicClock + 1, _useLogicClock);
    }
}

/// <summary>
/// The id of a synchronized table event.
/// <para>
/// Stores a timestamp with a guarenteed order and which source made it.
/// </para>
/// </summary>
/// <remarks>Laid out as <c>[ logicClock:47 | source:6 ]</c>.</remarks>
public readonly record struct SnowportId : IComparable<SnowportId>, IFormattable
{
    private readonly long ID;

    public static readonly SnowportId Empty = new(0);

    public SnowportId(long id)
    {
        ID = id;
    }

    public SnowportId(long logicClock, byte source)
        : this((logicClock << 6) | CheckSource(source)) { }

    /// <summary>
    /// The hybrid clock: the game time in milliseconds then an 8 bit increment for events in the same millisecond.
    /// </summary>
    public long logicClock => (ID >> 6) & 0x7FFFFFFFFFFFL;

    /// <summary>
    /// The source that made this SnowportId.
    /// </summary>
    public byte source => (byte)(ID & 0x3F);

    public int CompareTo(SnowportId other) => ID.CompareTo(other.ID);

    // Declared because comparisons were having to choose between long and ulong conversion.
    public static bool operator <(SnowportId left, SnowportId right) => left.ID < right.ID;

    public static bool operator >(SnowportId left, SnowportId right) => left.ID > right.ID;

    public static bool operator <=(SnowportId left, SnowportId right) => left.ID <= right.ID;

    public static bool operator >=(SnowportId left, SnowportId right) => left.ID >= right.ID;

    public static implicit operator long(SnowportId id) => id.ID;

    // An id only uses 53 bits, so it's never negative.
    public static implicit operator ulong(SnowportId id) => (ulong)id.ID;

    public static implicit operator Variant(SnowportId id) => id.ID;

    public static explicit operator SnowportId(Variant v) => new(v.AsInt64());

    public override string ToString() => ID.ToString();

    public string ToString(string format, IFormatProvider provider) =>
        ID.ToString(format, provider);

    public static SnowportId Parse(string s) => new(long.Parse(s));

    public static bool TryParse(string s, out SnowportId id)
    {
        if (long.TryParse(s, out var value))
        {
            id = new SnowportId(value);
            return true;
        }
        id = Empty;
        return false;
    }

    internal static byte CheckSource(byte source) =>
        source < Snowport.SourceCount
            ? source
            : throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "A source takes 6 bits."
            );
}

/// <summary>
/// A compact, timestamp-free unique ID for a <see cref="Replicated"/> record.
/// </summary>
/// <remarks>
/// <para>Laid out as <c>[ source:6 | counter:26 ]</c>.</para>
/// <para>
/// Records should refer to records with a SnowTag, never its value in an int or string.
/// The save file uses a form of garbage collection to keep records alive.
/// </para>
/// </remarks>
public readonly record struct SnowTag : IComparable<SnowTag>, IFormattable
{
    private readonly int ID;

    public static readonly SnowTag Empty = new(0);

    /// <summary>
    /// The maximum counter on any one source.
    /// </summary>
    public const int MaxCounter = (1 << 26) - 1;

    public SnowTag(int id)
    {
        ID = id;
    }

    public SnowTag(byte source, int counter)
        : this((SnowportId.CheckSource(source) << 26) | CheckCounter(counter)) { }

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
    public byte source => (byte)((ID >> 26) & 0x3F);

    /// <summary>
    /// The ID without the source number.
    /// </summary>
    public int counter => ID & MaxCounter;

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
