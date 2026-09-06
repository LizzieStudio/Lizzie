using System;
using System.Text.Json.Serialization;

/// <summary>
/// Where a reorder places a component in the stack.
/// <see cref="Unset"/> is only used by transform events
/// </summary>
public enum ZTarget
{
    Unset,
    Bottom,
    Top,
}

/// <summary>
/// A component's stacking order.
/// The effective order is derived by comparing these values:
/// 1. all <see cref="ZTarget.Top"/> components sit above all <see cref="ZTarget.Bottom"/> components
/// 2. the most recent <see cref="LastEvent"/> wins (top for Top, bottom for Bottom)
/// 3. components with a higher <see cref="Suborder"/> are on top
/// </summary>
public readonly struct ZOrder : IComparable<ZOrder>, IEquatable<ZOrder>
{
    [JsonPropertyName("t")]
    public ZTarget Target { get; }

    /// <summary>Separates components that share the same <see cref="LastEvent"/>. Higher is on top.</summary>
    [JsonPropertyName("s")]
    public int Suborder { get; }

    /// <summary>The id of the last event that reordered this component.</summary>
    [JsonPropertyName("e")]
    public SnowportId LastEvent { get; }

    [JsonConstructor]
    public ZOrder(ZTarget target, int suborder, SnowportId lastEvent)
    {
        Target = target;
        Suborder = suborder;
        LastEvent = lastEvent;
    }

    /// <summary>A value that always sorts below every normal component. Used by zones.</summary>
    public static ZOrder Floor => new(ZTarget.Bottom, int.MinValue, new SnowportId(ulong.MaxValue));

    private static int Rank(ZTarget t) =>
        t switch
        {
            ZTarget.Top => 2,
            ZTarget.Bottom => 1,
            _ => 0,
        };

    /// <summary>Positive when this component is higher in the stack than <paramref name="other"/>.</summary>
    public int CompareTo(ZOrder other)
    {
        if (Target != other.Target)
            return Rank(Target).CompareTo(Rank(other.Target));

        if (Target == ZTarget.Top)
        {
            // The more recent event is on top.
            var e = LastEvent.CompareTo(other.LastEvent);
            if (e != 0)
                return e;
        }
        else if (Target == ZTarget.Bottom)
        {
            // The more recent event was placed on the bottom more recently, so it is lower.
            var e = other.LastEvent.CompareTo(LastEvent);
            if (e != 0)
                return e;
        }

        // Same target and event: higher suborder is on top.
        return Suborder.CompareTo(other.Suborder);
    }

    public bool Equals(ZOrder other) =>
        Target == other.Target && Suborder == other.Suborder && LastEvent == other.LastEvent;

    public override bool Equals(object obj) => obj is ZOrder other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Target, Suborder, LastEvent);

    public override string ToString() => $"{Target}:{Suborder}:{LastEvent}";

    public static bool operator ==(ZOrder left, ZOrder right) => left.Equals(right);

    public static bool operator !=(ZOrder left, ZOrder right) => !left.Equals(right);

    public static bool operator <(ZOrder left, ZOrder right) => left.CompareTo(right) < 0;

    public static bool operator >(ZOrder left, ZOrder right) => left.CompareTo(right) > 0;

    public static bool operator <=(ZOrder left, ZOrder right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ZOrder left, ZOrder right) => left.CompareTo(right) >= 0;
}
