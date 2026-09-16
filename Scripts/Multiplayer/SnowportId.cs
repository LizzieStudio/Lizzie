using System;
using Godot;

/// <summary>
/// A hybrid timestamp and logic clock with a source ID to make it unique to the table.
/// </summary>
public class Snowport
{
    // Default to 0, the host.
    // Different source codes will be used when joining games.
    public static Snowport Clock = new Snowport(0);

    // start at 1 since 0 is used to mean Empty
    private ulong _useLogicClock = 1;
    private ulong _localOffsetMsec = 0;
    private ulong _globalOffsetMsec = 0;

    // A counter for SnowTag identities.
    private int _tagCounter = 1;

    public readonly byte source;

    public Snowport(byte source)
    {
        _localOffsetMsec = Time.GetTicksMsec();
        this.source = source;
    }

    /// <summary>
    /// Returns a clock under a new source that keeps the hybrid-clock and tag counter.
    /// Use this instead of <c>new Snowport(source)</c> when the local player switches source
    /// while keeping the current table (hosting or disconnecting).
    /// </summary>
    public Snowport WithSource(byte newSource)
    {
        return new Snowport(newSource)
        {
            _useLogicClock = _useLogicClock,
            _tagCounter = _tagCounter,
        };
    }

    /// <summary>
    /// Create a new SnowportId.
    /// </summary>
    /// <returns></returns>
    public SnowportId Create()
    {
        // If the game time has advanced, update the hybrid clock to match.
        var gameTime = Time.GetTicksMsec() - _localOffsetMsec + _globalOffsetMsec;
        _useLogicClock = Math.Max(gameTime << 8, _useLogicClock);
        var id = (_useLogicClock << 8) | source;
        _useLogicClock++;
        return new SnowportId(id);
    }

    /// <summary>
    /// Create a new <see cref="SnowTag"/>.
    /// </summary>
    public SnowTag CreateTag()
    {
        var tag = (source << 24) | _tagCounter;
        _tagCounter++;
        return new SnowTag(tag);
    }

    /// <summary>
    /// Advance the tag counter past <paramref name="tag"/> when it shares our source.
    /// </summary>
    public void ObserveTag(SnowTag tag)
    {
        if (tag.source != source)
            return;
        int counter = tag.Value & 0xFFFFFF;
        if (counter >= _tagCounter)
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
        var gameTime = Time.GetTicksMsec() - _localOffsetMsec + _globalOffsetMsec;
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
            // get 45 bits from 8 to 52
            return (ID >> 8) & 0x1FFFFFFFFFFFUL;
        }
    }

    public byte source
    {
        get
        {
            // get 8 bits from 0 to 7
            return (byte)ID;
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
/// A compact, timestamp-free unique ID for an <see cref="IReplicated"/>.
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

    /// <summary>The source that minted this tag. The host is 0, the rest are 1-255.</summary>
    public byte source => (byte)((ID >> 24) & 0xFF);

    public bool Equals(SnowTag other) => ID == other.ID;

    public override bool Equals(object other) => other is SnowTag tag && Equals(tag);

    public override int GetHashCode() => ID.GetHashCode();

    public int CompareTo(SnowTag other) => ID.CompareTo(other.ID);

    public static bool operator ==(SnowTag left, SnowTag right) => left.Equals(right);

    public static bool operator !=(SnowTag left, SnowTag right) => !left.Equals(right);

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
