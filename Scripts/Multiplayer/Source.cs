using System;
using Godot;

namespace Lizzie;

/// <summary>
/// The player who made a <see cref="SnowStamp"/> or <see cref="SnowTag"/>, unique among the players in a game.
/// </summary>
public readonly record struct Source
{
    /// <summary>
    /// The source of admin events, which belong to the project rather than a player.
    /// The host writes them and no one can undo them.
    /// This is used for things like loading the project.
    /// </summary>
    public static readonly Source Admin = new(0);

    /// <summary>
    /// The host's source. Joining players get the next ones.
    /// </summary>
    public static readonly Source Host = new(1);

    /// <summary>
    /// How many sources there can be, 0 to 63, since stamps and tags give the source 6 bits.
    /// </summary>
    public const int Count = 64;

    /// <summary>
    /// The local player's source, which the host assigns on joining a game.
    /// </summary>
    public static Source Me { get; set; } = Host;

    /// <summary>
    /// Whether this is the local player's source.
    /// </summary>
    public readonly bool IsMe()
    {
        return this == Me;
    }

    internal byte Value { get; }

    public Source(int value) =>
        Value = value is >= 0 and < Count
            ? (byte)value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A source takes 6 bits.");

    public static implicit operator Variant(Source source) => source.Value;

    public override string ToString() => Value.ToString();
}
