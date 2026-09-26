using System;
using Godot;

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

    public SnowTag ColumnId { get; set; }

    public event Action<SnowTag> Clicked;
    public event Action<SnowTag, Vector2> ContextRequested;
    public event Action<SnowTag, float> WidthDragged;
    public event Action<SnowTag, string> NameCommitted;
    public event Action<SnowTag, Vector2> ColumnDragMoved;
    public event Action<SnowTag, Vector2> ColumnDropped;

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

        var drag = new DragGesture(_label);
        drag.Moved += at => ColumnDragMoved?.Invoke(ColumnId, at);
        drag.Dropped += at => ColumnDropped?.Invoke(ColumnId, at);
        drag.Clicked += () => Clicked?.Invoke(ColumnId);
        drag.DoubleClicked += BeginRename;
        _label.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } b)
            {
                ContextRequested?.Invoke(ColumnId, b.GlobalPosition);
                _label.AcceptEvent();
            }
        };

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

    public void SetSelected(bool selected)
    {
        if (selected)
            AddThemeStyleboxOverride(
                "panel",
                new StyleBoxFlat { BgColor = Color.FromHtml("8cb1ff") with { A = 0.35f } }
            );
        else
            RemoveThemeStyleboxOverride("panel");
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
        NameCommitted?.Invoke(ColumnId, name);
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
            WidthDragged?.Invoke(ColumnId, _resizeStartWidth + delta);
        }
    }
}
