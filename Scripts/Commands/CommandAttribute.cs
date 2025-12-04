using Godot;
using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CommandAttribute : Attribute
{ 
    public CommandAttribute(VisualCommand command)
    {
        CommandKey = command;
    }
    
    public VisualCommand CommandKey { get; }

}
