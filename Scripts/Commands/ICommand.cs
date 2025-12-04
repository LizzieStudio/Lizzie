using Godot;
using System;
using System.Collections.Generic;

public interface ICommand 
{
    VisualCommand Command { get; set; }
    Update Execute(IEnumerable<VisualComponentBase> components, GameObjects context);
    
    SceneController SceneController { get; set; }
}
