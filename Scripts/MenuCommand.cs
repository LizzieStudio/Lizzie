using Godot;
using System;

public class MenuCommand
{
   public MenuCommand(VisualCommand command, bool isChecked = false, bool isEnabled = true, bool singleOnly = false)
   {
      Command = command;
      IsChecked = isChecked;
      IsEnabled = isEnabled;
      SingleOnly = singleOnly;
   }
   
   public VisualCommand Command { get; set; }
   public bool IsChecked { get; set; }
   public bool IsEnabled { get; set; }

    /// <summary>
    /// If true, command is only valid if only one component is selected
    /// </summary>
   public bool SingleOnly { get; set; }
}
