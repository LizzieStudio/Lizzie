using System;
using Godot;

/// <summary>
/// Press-and-drag on a control, reported in global coordinates. A press that
/// doesn't move past a few pixels is a click.
/// </summary>
public sealed class DragGesture
{
    private const float Threshold = 4f;

    private readonly Control _control;
    private Vector2 _pressAt;
    private bool _pressed;
    private bool _dragging;

    public event Action<Vector2> Moved;
    public event Action<Vector2> Dropped;

    /// <summary>A press and release without a drag, given the release.</summary>
    public event Action<InputEventMouseButton> Clicked;
    public event Action DoubleClicked;

    public DragGesture(Control control)
    {
        _control = control;
        _control.MouseFilter = Control.MouseFilterEnum.Stop;
        _control.GuiInput += OnInput;
    }

    private void OnInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
                DoubleClick: true
            }:
                _pressed = _dragging = false;
                DoubleClicked?.Invoke();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _pressed = true;
                    _pressAt = button.GlobalPosition;
                }
                else
                {
                    if (_dragging)
                        Dropped?.Invoke(button.GlobalPosition);
                    else if (_pressed)
                        Clicked?.Invoke(button);
                    _pressed = _dragging = false;
                }
                break;
            case InputEventMouseMotion motion when _pressed:
                if (!_dragging && motion.GlobalPosition.DistanceTo(_pressAt) < Threshold)
                    return;
                _dragging = true;
                Moved?.Invoke(motion.GlobalPosition);
                break;
            default:
                return;
        }
        _control.AcceptEvent();
    }
}
