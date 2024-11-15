using Godot;
using System;

public class MenuCommand
{
   public MenuCommand(VisualCommand command, bool isChecked = false, bool isEnabled = true)
   {
      Command = command;
      IsChecked = isChecked;
      IsEnabled = isEnabled;
   }
   
   public VisualCommand Command { get; set; }
   public bool IsChecked { get; set; }
   public bool IsEnabled { get; set; }
}
