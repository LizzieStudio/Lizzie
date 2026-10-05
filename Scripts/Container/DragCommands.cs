/// <summary>
/// Names for the events a drag and drop writes, so they can be told apart from other events.
/// No <see cref="Command"/> has them and they're never in <see cref="CommandList"/>,
/// so no menu, shortcut or Inspector dropdown offers them.
/// </summary>
public static class DragCommands
{
    /// <summary>
    /// Picking components up with the cursor.
    /// </summary>
    public static readonly CommandName Pickup = new("drag.pickup");

    /// <summary>
    /// Dropping components anywhere: onto the table, a drop target, or a hand.
    /// </summary>
    public static readonly CommandName Drop = new("drag.drop");
}
