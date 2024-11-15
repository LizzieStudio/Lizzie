using Godot;
using System;

public  class CommandResponse
{
    public CommandResponse(bool consumed, Change? undoAction)
    {
        Consumed = consumed;
        UndoAction = undoAction;
    }
    public bool Consumed { get; }
    public Change UndoAction { get;  }
}
