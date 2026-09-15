using System.Text.Json.Serialization;

/// <summary>
/// The cause of a TableEvent.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@")]
[JsonDerivedType(typeof(FlipAction), "f")]
[JsonDerivedType(typeof(RollAction), "r")]
[JsonDerivedType(typeof(DrawAction), "dr")]
[JsonDerivedType(typeof(DealAction), "dl")]
[JsonDerivedType(typeof(MoveAction), "m")]
[JsonDerivedType(typeof(ShuffleAction), "s")]
[JsonDerivedType(typeof(UndoAction), "u")]
[JsonDerivedType(typeof(GameStateSwitchAction), "gsw")]
public abstract class TableAction { }

/// <summary>Flips a token or deck.</summary>
public class FlipAction : TableAction { }

/// <summary>Rolls a die.</summary>
public class RollAction : TableAction { }

/// <summary>Draws components from a container to the board or a hand.</summary>
public class DrawAction : TableAction { }

/// <summary>Deals components round-robin to player hands.</summary>
public class DealAction : TableAction { }

/// <summary>Relocates components between table, container, and hand.</summary>
public class MoveAction : TableAction { }

/// <summary>Reorders a container's contents. The new order is carried by the event's transforms.</summary>
public class ShuffleAction : TableAction { }

/// <summary>Restores a snapshot.</summary>
public class GameStateSwitchAction : TableAction
{
    /// <summary>The snapshot to switch to.</summary>
    [JsonPropertyName("t")]
    public SnowTag Target { get; set; }
}

/// <summary>
/// Reverses a prior event, named by its <see cref="SnowportId"/>.
/// </summary>
public class UndoAction : TableAction
{
    [JsonPropertyName("t")]
    public SnowportId Target { get; set; }

    /// <summary>
    /// True to go forward. Redos should Target an Undo event (undo an undo).
    /// </summary>
    [JsonPropertyName("r")]
    public bool Redo { get; set; }
}
