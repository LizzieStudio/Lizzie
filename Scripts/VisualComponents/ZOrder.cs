using System;
using System.Text.Json.Serialization;

/// <summary>
/// Where a reorder places a component in the stack.
/// </summary>
public enum ZTarget
{
    Bottom,
    Top,
}

/// <summary>
/// A component's stacking order.
/// The effective order is derived by comparing these values:
/// 1. all <see cref="ZTarget.Top"/> components sit above all <see cref="ZTarget.Bottom"/> components
/// 2. the most recent <see cref="Stamp"/> wins (top for Top, bottom for Bottom)
/// 3. components with a higher <see cref="Suborder"/> are on top
/// </summary>
public readonly struct ZOrder : IComparable<ZOrder>, IEquatable<ZOrder>
{
    [JsonPropertyName("t")]
    public ZTarget Target { get; }

    /// <summary>Separates components that share the same <see cref="Stamp"/>. Higher is on top.</summary>
    [JsonPropertyName("s")]
    public int Suborder { get; }

    /// <summary>
    /// The SnowportId of the reorder that last reordered this component, shared by every component
    /// in that reorder. Components that share a Stamp should have unique Suborders.
    /// </summary>
    [JsonPropertyName("st")]
    public SnowportId Stamp { get; }

    [JsonConstructor]
    public ZOrder(ZTarget target, int suborder, SnowportId stamp)
    {
        Target = target;
        Suborder = suborder;
        Stamp = stamp;
    }

    /// <summary>A value that always sorts below every normal component. Used by zones.</summary>
    public static ZOrder Floor => new(ZTarget.Bottom, int.MinValue, new SnowportId(ulong.MaxValue));

    /// <summary>Positive when this component is higher in the stack than <paramref name="other"/>.</summary>
    public int CompareTo(ZOrder other)
    {
        if (Target != other.Target)
            return Target.CompareTo(other.Target);

        if (Target == ZTarget.Top)
        {
            // The more recent stamp is on top.
            var e = Stamp.CompareTo(other.Stamp);
            if (e != 0)
                return e;
        }
        else if (Target == ZTarget.Bottom)
        {
            // The more recent stamp was placed on the bottom more recently, so it is lower.
            var e = other.Stamp.CompareTo(Stamp);
            if (e != 0)
                return e;
        }

        // Same target and stamp: higher suborder is on top.
        return Suborder.CompareTo(other.Suborder);
    }

    public bool Equals(ZOrder other) =>
        Target == other.Target && Suborder == other.Suborder && Stamp == other.Stamp;

    public override bool Equals(object obj) => obj is ZOrder other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Target, Suborder, Stamp);

    public override string ToString() => $"{Target}:{Suborder}:{Stamp}";

    public static bool operator ==(ZOrder left, ZOrder right) => left.Equals(right);

    public static bool operator !=(ZOrder left, ZOrder right) => !left.Equals(right);

    public static bool operator <(ZOrder left, ZOrder right) => left.CompareTo(right) < 0;

    public static bool operator >(ZOrder left, ZOrder right) => left.CompareTo(right) > 0;

    public static bool operator <=(ZOrder left, ZOrder right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ZOrder left, ZOrder right) => left.CompareTo(right) >= 0;
}
