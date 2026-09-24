using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

/// <summary>
/// Definition dialog for a <see cref="VcZone"/>. Edits footprint size and the per-player
/// see/move rules that are stored on the zone's prototype parameters.
/// </summary>
public partial class ZonePanelDialogResult : ComponentPanelDialogResult
{
    private const int OptDefault = 0;
    private const int OptIncluded = 1;
    private const int OptExcluded = 2;

    private LineEdit _nameInput;
    private LineEdit _widthInput;
    private LineEdit _depthInput;
    private CheckBox _defaultIncluded;
    private CheckBox _hiddenWhenExcluded;
    private VBoxContainer _seatList;

    private readonly List<(Label Label, OptionButton Option)> _seatRows = new();

    private readonly Dictionary<int, int> _seatChoices = new();

    public override void _Ready()
    {
        ComponentType = VisualComponentBase.VisualComponentType.Zone;

        _nameInput = GetNode<LineEdit>("%ItemName");
        _widthInput = GetNode<LineEdit>("%Width");
        _depthInput = GetNode<LineEdit>("%Depth");
        _defaultIncluded = GetNode<CheckBox>("%DefaultIncluded");
        _hiddenWhenExcluded = GetNode<CheckBox>("%HiddenWhenExcluded");
        _seatList = GetNode<VBoxContainer>("%SeatList");
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        var players = R.Value<ProjectGameSettings>().Players;

        while (_seatRows.Count > players.Length)
        {
            _seatRows[^1].Label.GetParent().QueueFree();
            _seatRows.RemoveAt(_seatRows.Count - 1);
        }

        while (_seatRows.Count < players.Length)
            _seatRows.Add(AddSeatRow(_seatRows.Count));

        for (int i = 0; i < players.Length; i++)
        {
            _seatRows[i].Label.Text = string.IsNullOrEmpty(players[i].Name)
                ? $"Seat {i + 1}"
                : players[i].Name;
        }

        ShowSeatChoices();
    }

    private (Label, OptionButton) AddSeatRow(int seat)
    {
        var row = new HBoxContainer();

        var label = new Label
        {
            CustomMinimumSize = new Vector2(150, 0),
            TooltipText = "Sets the permissions for this seat.",
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row.AddChild(label);

        var option = new OptionButton { TooltipText = "Sets the permissions for this seat." };
        option.AddItem("Default", OptDefault);
        option.SetItemTooltip(OptDefault, "Follow the zone's 'Allow by default' setting.");
        option.AddItem("Allowed", OptIncluded);
        option.SetItemTooltip(OptIncluded, "This player can see and move the zone's contents.");
        option.AddItem("Block", OptExcluded);
        option.SetItemTooltip(
            OptExcluded,
            "This player cannot move the zone's contents.\nThey can still see the contents unless 'Hide contents' is checked."
        );
        row.AddChild(option);

        _seatList.AddChild(row);
        option.ItemSelected += index => _seatChoices[seat] = option.GetItemId((int)index);

        return (label, option);
    }

    private void ShowSeatChoices()
    {
        for (int i = 0; i < _seatRows.Count; i++)
            _seatRows[i].Option.Select(_seatChoices.GetValueOrDefault(i, OptDefault));
    }

    public override ComponentParameters GetParams()
    {
        var included = ImmutableArray.CreateBuilder<int>();
        var excluded = ImmutableArray.CreateBuilder<int>();

        for (int seat = 0; seat < _seatRows.Count; seat++)
        {
            switch (_seatChoices.GetValueOrDefault(seat, OptDefault))
            {
                case OptIncluded:
                    included.Add(seat);
                    break;
                case OptExcluded:
                    excluded.Add(seat);
                    break;
            }
        }

        return new ZoneParameters
        {
            ComponentName = _nameInput.Text,
            Width = ParamToFloat(_widthInput.Text),
            Depth = ParamToFloat(_depthInput.Text),
            DefaultIncluded = _defaultIncluded.ButtonPressed,
            HiddenWhenExcluded = _hiddenWhenExcluded.ButtonPressed,
            IncludedSeats = included.ToImmutable(),
            ExcludedSeats = excluded.ToImmutable(),
        };
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (ZoneParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _widthInput.Text = p.Width.ToString();
        _depthInput.Text = p.Depth.ToString();
        _defaultIncluded.ButtonPressed = p.DefaultIncluded;
        _hiddenWhenExcluded.ButtonPressed = p.HiddenWhenExcluded;

        _seatChoices.Clear();
        foreach (var seat in p.IncludedSeats)
            _seatChoices[seat] = OptIncluded;
        foreach (var seat in p.ExcludedSeats)
            _seatChoices[seat] = OptExcluded;

        ShowSeatChoices();
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = parameters as ZoneParameters;

        if (string.IsNullOrEmpty(p?.ComponentName))
            ret.Add("Name may not be blank");
        if ((p?.Width ?? 0) <= 0)
            ret.Add("Width must be > 0");
        if ((p?.Depth ?? 0) <= 0)
            ret.Add("Depth must be > 0");

        return ret;
    }
}
