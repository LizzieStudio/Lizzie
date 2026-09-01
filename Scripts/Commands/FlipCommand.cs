[Command(VisualCommand.Flip)]
public class FlipCommand : CommandBase
{
    public FlipCommand()
    {
        Caption = "Flip";
        Command = VisualCommand.Flip;
    }
}
