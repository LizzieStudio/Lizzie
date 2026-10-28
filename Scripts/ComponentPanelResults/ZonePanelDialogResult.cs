using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

namespace Lizzie;

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

    // The seat each row shows, in order.
    private readonly List<Ref<Seat>> _seats = new();

    private readonly Dictionary<Ref<Seat>, int> _seatChoices = new();

    public override void _Ready()
    {
        _nameInput = GetNode<LineEdit>("%ItemName");
        _widthInput = GetNode<LineEdit>("%Width");
        _depthInput = GetNode<LineEdit>("%Depth");
        _defaultIncluded = GetNode<CheckBox>("%DefaultIncluded");
        _hiddenWhenExcluded = GetNode<CheckBox>("%HiddenWhenExcluded");
        _seatList = GetNode<VBoxContainer>("%SeatList");
    }

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    private void Sync(IWatchReader R)
    {
        var seats = R.Get(Seat.Live);

        while (_seatRows.Count > seats.Count)
        {
            _seatRows[^1].Label.GetParent().QueueFree();
            _seatRows.RemoveAt(_seatRows.Count - 1);
        }

        while (_seatRows.Count < seats.Count)
            _seatRows.Add(AddSeatRow(_seatRows.Count));

        _seats.Clear();
        _seats.AddRange(seats.Select(s => s.Id));
        for (int i = 0; i < seats.Count; i++)
            _seatRows[i].Label.Text = seats[i].Label(i);

        ShowSeatChoices();
    }

    private (Label, OptionButton) AddSeatRow(int index)
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
        option.ItemSelected += item => _seatChoices[_seats[index]] = option.GetItemId((int)item);

        return (label, option);
    }

    private void ShowSeatChoices()
    {
        for (int i = 0; i < _seatRows.Count; i++)
            _seatRows[i].Option.Select(_seatChoices.GetValueOrDefault(_seats[i], OptDefault));
    }

    public override string PrototypeName => _nameInput.Text;

    public override ComponentParameters GetParams()
    {
        // Includes the rules for removed seats, which have no row.
        // This makes it where Undoing a delete of a seat brings back its zone rules.
        // The downside is that this will keep the rules in the save forever. We should fix that.
        ImmutableArray<Ref<Seat>> Chose(int choice) =>
            [.. _seatChoices.Where(c => c.Value == choice).Select(c => c.Key)];

        return new ZoneParameters
        {
            Width = ParamToFloat(_widthInput.Text),
            Depth = ParamToFloat(_depthInput.Text),
            DefaultIncluded = _defaultIncluded.ButtonPressed,
            HiddenWhenExcluded = _hiddenWhenExcluded.ButtonPressed,
            IncludedSeats = Chose(OptIncluded),
            ExcludedSeats = Chose(OptExcluded),
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

        if ((p?.Width ?? 0) <= 0)
            ret.Add("Width must be > 0");
        if ((p?.Depth ?? 0) <= 0)
            ret.Add("Depth must be > 0");

        return ret;
    }
}
