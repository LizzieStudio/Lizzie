using System;
using System.Collections.Generic;
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
    private ulong _useLogicClock = 1;
    private ulong _localOffsetMsec = 0;
    private ulong _globalOffsetMsec = 0;

    // A counter for SnowTag identities.
    private int _tagCounter = 1;

    public readonly byte source;

    /// <summary>How many sources there are, 0 to 63, since ids give the source 6 bits.</summary>
    public const int SourceCount = 64;

    public Snowport(byte source)
    {
        if (source >= SourceCount)
            throw new ArgumentOutOfRangeException(nameof(source), source, "A source takes 6 bits.");
        _localOffsetMsec = Time.GetTicksMsec();
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
    private ulong GameTimeMsec => Time.GetTicksMsec() - _localOffsetMsec + _globalOffsetMsec;

    /// <summary>
    /// The milliseconds since <paramref name="id"/> was created. Negative when it's in the future.
    /// </summary>
    public long MsecSince(SnowportId id) => (long)GameTimeMsec - (long)(id.logicClock >> 8);

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
        var id = (_useLogicClock << 6) | from;
        _useLogicClock++;
        return new SnowportId(id);
    }

    /// <summary>
    /// Create a new <see cref="SnowTag"/>.
    /// </summary>
    public SnowTag CreateTag()
    {
        if (_tagCounter > 0x3FFFFFF)
            throw new InvalidOperationException("This source has run out of SnowTags.");
        var tag = (source << 26) | _tagCounter;
        _tagCounter++;
        return new SnowTag(tag);
    }

    /// <summary>
    /// Advance the tag counter past <paramref name="tag"/> when it shares our source.
    /// </summary>
    public void ObserveTag(SnowTag tag)
    {
        int counter = tag.Value & 0x3FFFFFF;
        if (tag.source == source && counter >= _tagCounter)
            _tagCounter = counter + 1;
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

public readonly struct SnowportId : IEquatable<SnowportId>, IComparable<SnowportId>
{
    private readonly ulong ID;

    public ulong Value
    {
        get { return ID; }
    }

    public static readonly SnowportId Empty = new(0);

    public SnowportId(ulong ID)
    {
        this.ID = ID;
    }

    public ulong logicClock
    {
        get
        {
            // get 47 bits from 6 to 52
            return (ID >> 6) & 0x7FFFFFFFFFFFUL;
        }
    }

    public byte source
    {
        get
        {
            // get 6 bits from 0 to 5
            return (byte)(ID & 0x3F);
        }
    }

    public bool Equals(SnowportId other) => ID == other.ID;

    public override bool Equals(object other) => other is SnowportId Id && Equals(Id);

    public override int GetHashCode() => ID.GetHashCode();

    public int CompareTo(SnowportId other) => ID.CompareTo(other.ID);

    public static bool operator ==(SnowportId left, SnowportId right) => left.Equals(right);

    public static bool operator !=(SnowportId left, SnowportId right) => !left.Equals(right);

    public override string ToString() => ID.ToString();

    public static SnowportId Parse(string s) => new SnowportId(ulong.Parse(s));

    public static bool TryParse(string s, out SnowportId id)
    {
        if (ulong.TryParse(s, out var value))
        {
            id = new SnowportId(value);
            return true;
        }
        id = Empty;
        return false;
    }
}

/// <summary>
/// A compact, timestamp-free unique ID for a <see cref="Replicated"/> record.
/// </summary>
/// <remarks>
/// Records should refer to records with a SnowTag, never its value in an int or string.
/// The save file uses a form of garbage collection to keep records alive.
/// </remarks>
public readonly struct SnowTag : IEquatable<SnowTag>, IComparable<SnowTag>
{
    private readonly int ID;

    public int Value => ID;

    public static readonly SnowTag Empty = new(0);

    public SnowTag(int ID)
    {
        this.ID = ID;
    }

    /// <summary>
    /// The source that minted this tag.
    /// </summary>
    public byte source => (byte)((ID >> 26) & 0x3F);

    public bool Equals(SnowTag other) => ID == other.ID;

    public override bool Equals(object other) => other is SnowTag tag && Equals(tag);

    public override int GetHashCode() => ID.GetHashCode();

    public int CompareTo(SnowTag other) => ID.CompareTo(other.ID);

    public static bool operator ==(SnowTag left, SnowTag right) => left.Equals(right);

    public static bool operator !=(SnowTag left, SnowTag right) => !left.Equals(right);

    public static implicit operator SnowTag(int x) => new SnowTag(x);

    public static implicit operator int(SnowTag x) => x.ID;

    public override string ToString() => ID.ToString();

    public static SnowTag Parse(string s) => new SnowTag(int.Parse(s));

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
