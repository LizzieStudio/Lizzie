using System;
using Godot;

namespace Lizzie;

/// <summary>
/// A dataset column header. Click the name to select the column, drag it to reorder,
/// double-click it to rename, drag the right edge to resize.
/// </summary>
public partial class HeaderCell : PanelContainer
{
    private Label _label;
    private LineEdit _nameEdit;
    private Panel _resizeHandle;
    private bool _isResizing;
    private float _resizeStartX;
    private float _resizeStartWidth;

    private string _headerText = string.Empty;

    public Ref<DataColumn> ColumnRef { get; set; }

    /// <summary>A click on the name, with the modifiers held.</summary>
    public event Action<Ref<DataColumn>, InputEventMouseButton> Clicked;
    public event Action<Ref<DataColumn>> RightPressed;
    public event Action<Ref<DataColumn>, float> WidthDragged;
    public event Action<Ref<DataColumn>, string> NameCommitted;

    // Set by a double-click, so the release that follows it isn't a click.
    private bool _doubleClicked;

    public override void _Ready()
    {
        var hbox = new HBoxContainer();
        AddChild(hbox);

        _label = new Label();
        hbox.AddChild(_label);
        _label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _label.Text = _headerText;
        _label.AddThemeColorOverride("font_color", Color.FromHtml("8cb1ff"));
        _label.MouseDefaultCursorShape = CursorShape.Move;
        _label.TooltipText = "Click to select, drag to reorder, double-click to rename";
        _label.MouseFilter = MouseFilterEnum.Stop;
        _label.GuiInput += OnLabelInput;
        // Dragging the name moves the column, through Godot's drag and drop.
        _label.SetDragForwarding(
            Callable.From<Vector2, ReorderDrag>(_ => new ReorderDrag
            {
                Axis = Vector2.Axis.X,
                MovedRef = ColumnRef.Tag,
            }),
            default,
            default
        );

        _nameEdit = new LineEdit();
        hbox.AddChild(_nameEdit);
        _nameEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _nameEdit.Visible = false;
        _nameEdit.TextSubmitted += _ => EndRename(true);
        _nameEdit.FocusExited += () => EndRename(true);
        _nameEdit.GuiInput += OnNameEditInput;

        _resizeHandle = new Panel();
        hbox.AddChild(_resizeHandle);
        _resizeHandle.CustomMinimumSize = new Vector2(8, 0);
        _resizeHandle.MouseDefaultCursorShape = CursorShape.Hsize;
        _resizeHandle.SizeFlagsVertical = SizeFlags.ExpandFill;

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        _resizeHandle.AddThemeStyleboxOverride("panel", styleBox);

        _resizeHandle.GuiInput += OnResizeHandleInput;
    }

    public void SetHeaderText(string text)
    {
        _headerText = text ?? string.Empty;
        if (_label != null)
            _label.Text = _headerText;
    }

    /// <summary>Swaps the name for a text box with the whole name selected.</summary>
    public void BeginRename()
    {
        _nameEdit.Text = _headerText;
        _label.Visible = false;
        _nameEdit.Visible = true;
        _nameEdit.GrabFocus();
        _nameEdit.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_nameEdit.Visible)
            return;

        // Hiding the focused box re-enters through FocusExited, which the check above stops.
        _nameEdit.Visible = false;
        _label.Visible = true;

        var name = _nameEdit.Text.Trim();
        if (!commit || name.Length == 0 || name == _headerText)
            return;

        SetHeaderText(name);
        NameCommitted?.Invoke(ColumnRef, name);
    }

    private void OnLabelInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
                DoubleClick: true
            }:
                _doubleClicked = true;
                BeginRename();
                break;
            // A drag takes the release from the label, so one that gets here is a click.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release:
                if (!_doubleClicked)
                    Clicked?.Invoke(ColumnRef, release);
                _doubleClicked = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                RightPressed?.Invoke(ColumnRef);
                break;
            default:
                return;
        }
        _label.AcceptEvent();
    }

    private void OnNameEditInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            EndRename(false);
            _nameEdit.AcceptEvent();
        }
    }

    private void OnResizeHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            _isResizing = mouseButton.Pressed;
            _resizeStartX = mouseButton.GlobalPosition.X;
            _resizeStartWidth = CustomMinimumSize.X;
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isResizing)
        {
            float delta = mouseMotion.GlobalPosition.X - _resizeStartX;
            WidthDragged?.Invoke(ColumnRef, _resizeStartWidth + delta);
        }
    }
}
