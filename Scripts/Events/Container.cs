using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Where a <see cref="ComponentPosition"/> could be.
/// </summary>
[JsonPolymorphic]
public abstract record Container;

/// <summary>
/// Inside another component, like a card in a deck or a token in a bag.
/// </summary>
[JsonName("InComponent")]
public sealed record InComponent(Ref<Component> Component) : Container;

/// <summary>
/// In a seat's hand.
/// </summary>
[JsonName("InHand")]
public sealed record InHand(Ref<Seat> Seat) : Container;

/// <summary>
/// Being dragged by the player in a seat.
/// </summary>
/// <param name="OverHand">
/// Whether it's hovering over their hand rather than the table.
/// These cards are still hidden to all other players.
/// </param>
[JsonName("Held")]
public sealed record Held(Ref<Seat> Seat, bool OverHand) : Container;

/// <summary>
/// A key for the container as it exists in one stage.
/// </summary>
public readonly record struct ContainerKey(Ref<Stage> StageRef, Container Container)
{
    /// <summary>
    /// Gets the container key for <paramref name="component"/>.
    /// </summary>
    public static ContainerKey Of(StageKey<ComponentPosition> component) =>
        new(component.StageRef, new InComponent(component.ComponentRef));
}
