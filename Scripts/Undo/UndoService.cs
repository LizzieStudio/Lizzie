using Godot;
using System;
using System.Collections.Generic;

public partial class UndoService: Node
{
    // Singleton Pattern
    private static UndoService _instance;
    public static UndoService Instance => _instance;


    private Stack<Update> _undoStack = new();

    private Stack<Update> _redoStack = new();
    
    private int _maxStackSize = 100;
    private int _minStackSize = 50;

    public override void _Ready()
    {
        base._Ready();
        _instance = this;
    }

    public void ClearStack()
    {
        
    }

    public void Add(Update update)
    {
        if (_undoStack.Count > _maxStackSize) TrimStack(_undoStack, _minStackSize);
        _undoStack.Push(update);
    }

    public void Add(Change change)
    {
        var u = new Update();
        u.Add(change);
        Add(u);
    }

    private void TrimStack(Stack<Update> stack, int size)
    {
        if (size >= stack.Count) return;
        
        var arr = stack.ToArray();

        stack.Clear();

        for (int i = arr.Length; i > 0; i--)
        {
            if (i < size)
            {
                stack.Push(arr[i-1]);
            }
            else
            {
                Finalize(arr[i-1]); //mainly permanently deleting objects
            }
        }
    }

    private void Finalize(Update update)
    {
        foreach (var c in update)
        {
            if (c.Action == Change.ChangeType.Deletion)
            {
                c.Component.QueueFree();
            }
        }        
    }

    public void Add(VisualComponentBase component, Change.ChangeType changeType, object beginState,
        object endState)
    {
        var change = new Change
        {
            Action = changeType,
            Component = component,
            Begin = beginState,
            End = endState
        };
        Add(change);
    }

    public void Undo()
    {
        if (!_undoStack.TryPop(out var update)) return;

        foreach (var c in update)
        {
            switch (c.Action)
            {
                case Change.ChangeType.Transform:
                    ExecuteTransform(c);
                    break;
                case Change.ChangeType.Creation:
                    c.Component.Visible = false;    //hide, don't delete for now. In case we have a 'redo' function
                    break;
                case Change.ChangeType.Deletion:
                    c.Component.Visible = true;     //do we also want to move this out of GameObjects and into a different list?
                                                    //and do we have to adjust z-order?
                    break;
                case Change.ChangeType.LockStatus:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private void ExecuteTransform(Change change)
    {
        if (change.Begin is Transform3D t)
        {
            change.Component.Transform = t;
        }
    }

    public void BeginUpdate()
    {
    }

    public void CommitUpdate()
    {
    }
}