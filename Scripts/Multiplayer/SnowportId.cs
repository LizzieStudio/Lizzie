using System;
using Godot;

/// <summary>
/// A hybrid timestamp and logic clock with a source ID to make it unique to the table.
/// </summary>
public class Snowport
{
    public static Snowport Clock;

    // start at 1 since 0 is used to mean Empty
    private ulong _useLogicClock = 1;
    private ulong _localOffsetMsec = 0;
    private ulong _globalOffsetMsec = 0;
    public readonly byte source;

    public Snowport(byte source)
    {
        _localOffsetMsec = Time.GetTicksMsec();
        this.source = source;
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
    /// Process a SnowportId from another source to update the hybrid clock.
    /// </summary>
    /// <param name="id">The id to consume.</param>
    public void Process(SnowportId id)
    {
        if (id.source == source)
            return;
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

    public readonly static SnowportId Empty = new(0);

    public SnowportId(ulong ID)
    {
        this.ID = ID;
    }

    public ulong logicClock
    {
        get
        {
            // get 44 bits from 8 to 51
            return (ID >> 8) & 0xFFFFFFFFFFFUL;
        }
    }

    public byte source
    {
        get
        {
            // get the last 8 bits
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
}
