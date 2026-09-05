using System;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The cause of a TableEvent.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FlipAction), "Flip")]
[JsonDerivedType(typeof(RollAction), "Roll")]
[JsonDerivedType(typeof(DragAction), "Drag")]
[JsonDerivedType(typeof(DropAction), "Drop")]
[JsonDerivedType(typeof(DrawAction), "Draw")]
[JsonDerivedType(typeof(DealAction), "Deal")]
[JsonDerivedType(typeof(MoveAction), "Move")]
[JsonDerivedType(typeof(ShuffleAction), "Shuffle")]
[JsonDerivedType(typeof(PlayerJoinAction), "PlayerJoin")]
[JsonDerivedType(typeof(PlayerLeaveAction), "PlayerLeave")]
public abstract class TableAction { }

/// <summary>Flips a token or deck.</summary>
public class FlipAction : TableAction
{
    public SnowportId ComponentRef { get; set; }

    public bool FaceUp { get; set; }
}

/// <summary>Rolls a die to a chosen face.</summary>
public class RollAction : TableAction
{
    public SnowportId ComponentRef { get; set; }

    public int Side { get; set; }
}

/// <summary>
/// Begins a drag.
/// </summary>
public class DragAction : TableAction
{
    public DraggedComponent[] Components { get; set; } = Array.Empty<DraggedComponent>();
}

public struct DraggedComponent
{
    public SnowportId ComponentRef { get; set; }

    /// <summary>
    /// Table offset of the component from the dragging player's cursor during the drag.
    /// The component's position is the source player's cursor plus this offset.
    /// </summary>
    public Vector2 Offset { get; set; }
}

/// <summary>Ends a drag.</summary>
public class DropAction : TableAction { }

/// <summary>Draws components from a container to the board or a hand.</summary>
public class DrawAction : TableAction { }

/// <summary>Deals components round-robin to player hands.</summary>
public class DealAction : TableAction { }

/// <summary>Relocates components between table, container, and hand.</summary>
public class MoveAction : TableAction { }

/// <summary>Reorders a container's contents. The new order is carried by the event's transforms.</summary>
public class ShuffleAction : TableAction { }

/// <summary>
/// Fired when a player claims a seat.
/// </summary>
public class PlayerJoinAction : TableAction
{
    public int Seat { get; set; }

    public int PeerId { get; set; }

    /// <summary>The container id for this player's hand.</summary>
    public SnowportId HandRef { get; set; }
}

/// <summary>
/// Fired when a player leaves a seat.
/// TODO Hands are orphaned. A new solution is necessary.
/// </summary>
public class PlayerLeaveAction : TableAction
{
    public int Seat { get; set; }

    public int PeerId { get; set; }
}
