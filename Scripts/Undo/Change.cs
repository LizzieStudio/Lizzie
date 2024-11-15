using Godot;
using System;

public class Change
{
    public VisualComponentBase Component { get; set; }

    public enum ChangeType
    {
        Transform,
        Creation,
        Deletion,
        LockStatus
    }
    
    public object Begin { get; set; }
    public object End { get; set; }
    public ChangeType Action { get; set; }
}
