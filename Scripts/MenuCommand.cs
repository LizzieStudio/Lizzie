using System;
using Godot;

public class MenuCommand
{
    public MenuCommand(CommandBase command)
    {
        Init(command);
    }

    public MenuCommand(VisualCommand command)
    {
        ProjectService.Instance.CommandDictionary.TryGetValue(command, out var commandBase);
        if (commandBase == null)
            return;

        Init(commandBase);
    }

    private void Init(CommandBase command)
    {
        Command = command.Command;
        IsChecked = command.IsToggled;
        IsEnabled = true;
        SingleOnly = command.SingleOnly;
        Caption = command.Caption;
    }

    public VisualCommand Command { get; set; }
    public bool IsChecked { get; set; }
    public bool IsEnabled { get; set; }

    public string Caption { get; set; }

    /// <summary>
    /// If true, command is only valid if only one component is selected
    /// </summary>
    public bool SingleOnly { get; set; }

    public bool AddQtySubmenu { get; set; }
}
