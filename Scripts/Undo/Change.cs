using System;
using Godot;

public class Change
{
    public VisualComponentBase Component { get; set; }

    public enum ChangeType
    {
        Transform,
        LockStatus,
        Layer,
    }

    public object Begin { get; set; }
    public object End { get; set; }
    public ChangeType Action { get; set; }
}
