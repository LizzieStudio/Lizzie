using System;
using System.Collections.Generic;
using Godot;

public abstract class CommandBase : ICommand
{
    public virtual VisualCommand Command { get; set; }
    public abstract Update Execute(
        IEnumerable<VisualComponentBase> components,
        GameObjects context
    );

    public SceneController SceneController { get; set; }
}
