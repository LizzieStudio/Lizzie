using System;
using System.Threading;
using Godot;

namespace Lizzie;

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
    /// The low bits of <see cref="LogicClock"/> that increment for stamps made in the same millisecond.
    /// </summary>
    public const int TickBits = 6;

    /// <summary>
    /// The engine's time in milliseconds. Tests replace it, since they can't call the engine.
    /// </summary>
    public static Func<long> TicksMsec { get; set; } = () => (long)Time.GetTicksMsec();

    // Tests sometimes use parallel threads.
    private static readonly Lock Lock = new();

    // the hybrid clock of the next stamp, starting at 1 since 0 is used to mean Empty
    private static long _next = 1;

    // what to add to TicksMsec for the game time
    private static long _offsetMsec;

    /// <summary>
    /// The project's shared time, in milliseconds.
    /// </summary>
    private static long GetGameTimeMsec()
    {
        return TicksMsec() + _offsetMsec;
    }

    /// <summary>
    /// Create a new SnowStamp for the local player.
    /// </summary>
    public static SnowStamp Create() => Next(Source.Me);

    /// <summary>
    /// Create a new SnowStamp for an admin event.
    /// Only the host or a solo player can make them.
    /// </summary>
    public static SnowStamp CreateAdmin() =>
        Source.Host.IsMe()
            ? Next(Source.Admin)
            : throw new Exception("Only the host can make admin events");

    private static SnowStamp Next(Source from)
    {
        lock (Lock)
        {
            // If the game time has advanced, update the hybrid clock to match.
            _next = Math.Max(GetGameTimeMsec() << TickBits, _next);
            return new SnowStamp(_next++, from);
        }
    }

    /// <summary>
    /// Advance the clock past <paramref name="stamp"/>, from another player or a load, so the next stamp made is newer.
    /// </summary>
    public static void Observe(SnowStamp stamp)
    {
        lock (Lock)
        {
            // If the stamp's time appears to be in the future,
            // adjust the game time to match it.
            var gameTime = GetGameTimeMsec();
            var otherClock = stamp.LogicClock >> TickBits;
            if (otherClock > gameTime)
                _offsetMsec += otherClock - gameTime;

            // Make sure the next stamp's hybrid clock is greater than this one's.
            _next = Math.Max(stamp.LogicClock + 1, _next);
        }
    }

    /// <summary>
    /// Starts the game time and the hybrid clock over, for a new timeline.
    /// </summary>
    internal static void Restart()
    {
        lock (Lock)
        {
            _offsetMsec = -TicksMsec();
            _next = 1;
        }
    }

    public SnowStamp(long id)
    {
        ID = id;
    }

    public SnowStamp(long logicClock, Source source)
        : this((logicClock << 6) | source.Value) { }

    /// <summary>
    /// The hybrid clock: the game time in milliseconds then a <see cref="TickBits"/> bit increment for events in the same millisecond.
    /// </summary>
    public long LogicClock => (ID >> 6) & 0x7FFFFFFFFFFFL;

    /// <summary>
    /// The source that made this SnowStamp.
    /// </summary>
    public Source Source => new((int)(ID & 0x3F));

    /// <summary>
    /// The milliseconds since this stamp was made. Negative when it's in the future.
    /// </summary>
    public readonly long GetAgeMsec()
    {
        return GetGameTimeMsec() - (LogicClock >> TickBits);
    }

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
