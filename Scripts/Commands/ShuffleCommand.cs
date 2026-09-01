[Command(VisualCommand.Shuffle)]
public class ShuffleCommand : CommandBase
{
    public ShuffleCommand()
    {
        Caption = "Shuffle";
        Command = VisualCommand.Shuffle;
    }
}
