using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// The window for the project's seats, which writes every edit as it's made.
/// </summary>
public partial class SeatsWindow : Window, ICommandView
{
    private const string RowScene = "res://Scenes/Project/PlayerDefinition.tscn";

    private VBoxContainer _list;

    // Takes a dragged seat anywhere over the seats, but only during a seat drag.
    private Control _dropZone;

    // Where a dragged seat would land.
    private ColorRect _dropLine;

    // The seat being dragged, while it is.
    private SeatDrag _drag;

    private ChildList<Ref<Seat>, PlayerDefinition> _rows;

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    public override void _Ready()
    {
        _list = GetNode<VBoxContainer>("%SeatList");
        _rows = new(
            _list,
            _ =>
            {
                var row = GD.Load<PackedScene>(RowScene).Instantiate<PlayerDefinition>();
                _list.AddChild(row);
                return row;
            }
        );
        _dropZone = GetNode<Control>("%SeatDropZone");
        _dropLine = GetNode<ColorRect>("%SeatDropLine");
        GetNode<Button>("%AddSeat").Pressed += AddSeat;
        _dropZone.SetDragForwarding(
            default,
            Callable.From<Vector2, Variant, bool>((_, data) => data.As<GodotObject>() is SeatDrag),
            Callable.From<Vector2, Variant>(
                (at, data) =>
                    DropSeat(
                        ((SeatDrag)data.As<GodotObject>()).Seat,
                        _dropZone.GetGlobalTransform() * at
                    )
            )
        );
        CloseRequested += QueueFree;
    }

    /// <summary>
    /// Keeps a row for each seat, in order.
    /// </summary>
    private void Sync(IWatchReader R)
    {
        var seats = R.Get(Seat.Live);
        var rows = _rows.Show(seats, s => s.Id);
        for (int i = 0; i < seats.Count; i++)
            rows[i].ShowSeat(seats[i], i);
    }

    /// <summary>
    /// Adds a new seat after the others.
    /// </summary>
    private void AddSeat()
    {
        var seats = Atoms.R.Get(Seat.Live);
        Atoms.R.Write(this, Seat.New(SnowRank.At(seats, seats.Count), seats));
    }

    public override void _Notification(int what)
    {
        // Godot tells every node when any drag begins and ends.
        if (what == NotificationDragBegin)
        {
            _drag = GetViewport().GuiGetDragData().As<GodotObject>() as SeatDrag;
            if (_drag != null)
                _dropZone.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        else if (what == NotificationDragEnd && _drag != null)
        {
            _drag = null;
            _dropZone.MouseFilter = Control.MouseFilterEnum.Ignore;
            _dropLine.Visible = false;
        }
    }

    public override void _Process(double delta)
    {
        if (_drag != null)
            ShowDrop(_dropZone.GetGlobalMousePosition());
    }

    private List<PlayerDefinition> Rows() =>
        _list.GetChildren().OfType<PlayerDefinition>().ToList();

    // Where a dragged seat would land: how many of the rows are above the pointer.
    private int DropIndex(Vector2 at) =>
        Rows().Count(row => row.GetGlobalRect().GetCenter().Y < at.Y);

    /// <summary>
    /// Shows a line across the seats where a dragged seat would land.
    /// </summary>
    private void ShowDrop(Vector2 at)
    {
        var rows = Rows();
        if (rows.Count == 0)
            return;

        int index = DropIndex(at);
        float line =
            index < rows.Count
                ? rows[index].GetGlobalRect().Position.Y
                : rows[^1].GetGlobalRect().End.Y;
        var zone = _dropZone.GetGlobalRect();
        float thickness = _dropLine.CustomMinimumSize.Y;
        _dropLine.Position = new Vector2(
            zone.Position.X,
            Mathf.Clamp(line, zone.Position.Y, zone.End.Y) - thickness / 2
        );
        _dropLine.Size = new Vector2(zone.Size.X, thickness);
        _dropLine.Visible = true;
    }

    /// <summary>
    /// Moves the seat <paramref name="moved"/> to where it was dropped, at <paramref name="at"/> in the window.
    /// </summary>
    private void DropSeat(Ref<Seat> moved, Vector2 at)
    {
        var seats = Atoms.R.Get(Seat.Live);
        int from = seats.FindIndex(s => s.Id == moved);
        int to = DropIndex(at);
        if (from < 0 || to == from || to == from + 1)
            return;

        seats.RemoveAt(from);
        var rank = SnowRank.At(seats, to > from ? to - 1 : to);
        Atoms.R.Update(this, Seat.ById, moved, s => s with { Rank = rank });
    }
}
