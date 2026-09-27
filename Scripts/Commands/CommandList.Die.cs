/// <summary>Commands for dice.</summary>
public static partial class CommandList
{
    public static readonly Command RollDie = new ComponentCommand
    {
        Id = new("die.roll"),
        Caption = "Roll {0}",
        Noun = ("Die", "Dice"),
        Shortcut = "roll",
        AppliesTo = c => c is VcDie,
        Action = (cs, _) => Send(VisualCommand.Roll, cs),
    };

    // The number keys also draw cards, so they're not in the menu here.
    public static readonly Command SetDieFace = new ComponentCommand
    {
        Id = new("die.set_face"),
        Caption = "Set Die Face",
        NumberKeys = true,
        ShowInMenu = false,
        AppliesTo = c => c is VcDie,
        Action = (cs, n) => Send(VisualCommand.Num1 + (n - 1), cs),
    };
}
