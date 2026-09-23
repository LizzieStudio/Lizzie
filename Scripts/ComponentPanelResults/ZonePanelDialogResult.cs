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

    // seatIndex -> the per-seat option button
    private readonly Dictionary<int, OptionButton> _seatOptions = new();

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

    public override void Activate()
    {
        RebuildSeatList();
    }

    private void RebuildSeatList()
    {
        foreach (var child in _seatList.GetChildren())
            child.QueueFree();
        _seatOptions.Clear();

        var settings = (CurrentProject ?? ProjectService.Instance.CurrentProject)?.GameSettings;
        if (settings == null)
            return;
        var players = settings.Players;

        for (int i = 0; i < players.Length; i++)
        {
            var row = new HBoxContainer();

            var label = new Label
            {
                Text = string.IsNullOrEmpty(players[i].Name) ? $"Seat {i + 1}" : players[i].Name,
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
            _seatOptions[i] = option;
        }
    }

    public override ComponentParameters GetParams()
    {
        var included = ImmutableArray.CreateBuilder<int>();
        var excluded = ImmutableArray.CreateBuilder<int>();

        foreach (var kv in _seatOptions)
        {
            switch (kv.Value.GetSelectedId())
            {
                case OptIncluded:
                    included.Add(kv.Key);
                    break;
                case OptExcluded:
                    excluded.Add(kv.Key);
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

    public override void DisplayPrototype(SnowTag prototypeId)
    {
        DisplayPrototype(ProjectService.Instance.Prototypes.Records[prototypeId]);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        RebuildSeatList();

        var p = (ZoneParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _widthInput.Text = p.Width.ToString();
        _depthInput.Text = p.Depth.ToString();
        _defaultIncluded.ButtonPressed = p.DefaultIncluded;
        _hiddenWhenExcluded.ButtonPressed = p.HiddenWhenExcluded;

        var included = new HashSet<int>(p.IncludedSeats);
        var excluded = new HashSet<int>(p.ExcludedSeats);

        foreach (var kv in _seatOptions)
        {
            if (excluded.Contains(kv.Key))
                kv.Value.Select(OptExcluded);
            else if (included.Contains(kv.Key))
                kv.Value.Select(OptIncluded);
            else
                kv.Value.Select(OptDefault);
        }
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
