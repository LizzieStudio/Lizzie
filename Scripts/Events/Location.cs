using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Where a <see cref="ComponentPosition"/> is when it's not on the table:
/// in a container, in a hand, or being dragged.
/// </summary>
[JsonPolymorphic]
public abstract record Location;

/// <summary>
/// Inside a container, which is another component, like a card in a deck or a token in a bag.
/// </summary>
[JsonName("InComponent")]
public sealed record InComponent(Ref<Component> Component) : Location;

/// <summary>
/// In a seat's hand.
/// </summary>
[JsonName("InHand")]
public sealed record InHand(Ref<Seat> Seat) : Location;

/// <summary>
/// Being dragged by the player in a seat.
/// </summary>
/// <param name="OverHand">
/// Whether it's hovering over their hand rather than the table.
/// These cards are still hidden to all other players.
/// </param>
[JsonName("Dragged")]
public sealed record Dragged(Ref<Seat> Seat, bool OverHand) : Location;

/// <summary>
/// A location in one stage, which <see cref="ComponentPosition.ByLocation"/> lists positions at.
/// </summary>
public readonly record struct LocationKey(Ref<Stage> StageRef, Location Location)
{
    /// <summary>
    /// The location inside <paramref name="component"/>, where its contents are.
    /// </summary>
    public static LocationKey Inside(PositionKey component) =>
        new(component.StageRef, new InComponent(component.ComponentRef));
}
