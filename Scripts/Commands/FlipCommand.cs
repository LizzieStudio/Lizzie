using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

[Command(VisualCommand.Num1)]
[Command(VisualCommand.Flip)]
public class FlipCommand : BasicCommand
{
}

[Command(VisualCommand.Freeze)]
public class Freeze : CommandBase
{
    public override Update Execute(IEnumerable<VisualComponentBase> components, GameObjects context)
    {
        return new Update();
    }
}


