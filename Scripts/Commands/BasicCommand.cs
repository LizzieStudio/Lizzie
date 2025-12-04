using Godot;
using System;
using System.Collections.Generic;

public abstract class BasicCommand : CommandBase
{
    public override Update Execute(IEnumerable<VisualComponentBase> components, GameObjects context)
    {
        Update update = new();

        foreach (var c in components)
        {
            var change = c.ProcessCommand(Command);
            if (!change.Consumed) continue;
            if (change.UndoAction != null) update.Add(change.UndoAction);
        }

        return update;
    }
}
