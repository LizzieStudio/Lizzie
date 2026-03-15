using System;
using Godot;

public partial class PosSizeBlock : Node
{
    private LineEdit _x;
    private LineEdit _y;
    private LineEdit _width;
    private LineEdit _height;
    private LineEdit _rotation;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _x = GetNode<LineEdit>("%PosX");
        _y = GetNode<LineEdit>("%PosY");
        _rotation = GetNode<LineEdit>("%Rotation");
        _width = GetNode<LineEdit>("%PosW");
        _height = GetNode<LineEdit>("%PosH");

        _x.TextChanged += _ => OnPosUpdated();
        _y.TextChanged += _ => OnPosUpdated();
        _rotation.TextChanged += _ => OnPosUpdated();
        _width.TextChanged += _ => OnPosUpdated();
        _height.TextChanged += _ => OnPosUpdated();
    }

    private void OnPosUpdated()
    {
        PosUpdated?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler PosUpdated;

    public int X
    {
        get
        {
            int.TryParse(_x.Text, out var x);
            return x;
        }
        set => _x.Text = value.ToString();
    }

    public int Y
    {
        get
        {
            int.TryParse(_y.Text, out var x);
            return x;
        }
        set => _y.Text = value.ToString();
    }

    public int Width
    {
        get
        {
            int.TryParse(_width.Text, out var x);
            return x;
        }
        set => _width.Text = value.ToString();
    }

    public int Height
    {
        get
        {
            int.TryParse(_height.Text, out var x);
            return x;
        }
        set => _height.Text = value.ToString();
    }

    public int Rotation
    {
        get
        {
            int.TryParse(_rotation.Text, out var x);
            return x;
        }
        set => _rotation.Text = value.ToString();
    }
}
