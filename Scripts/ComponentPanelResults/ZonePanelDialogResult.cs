using System;
using System.Collections.Generic;
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

        var players = (CurrentProject ?? ProjectService.Instance.CurrentProject)
            ?.GameSettings
            ?.Players;
        if (players == null)
            return;

        for (int i = 0; i < players.Count; i++)
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

            var option = new OptionButton
            {
                TooltipText = "Sets the permissions for this seat."
            };
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

    public override Dictionary<string, object> GetParams()
    {
        var d = new Dictionary<string, object>
        {
            { "ComponentName", _nameInput.Text },
            { VcZone.WidthKey, ParamToFloat(_widthInput.Text) },
            { VcZone.DepthKey, ParamToFloat(_depthInput.Text) },
            { VcZone.DefaultIncludedKey, _defaultIncluded.ButtonPressed },
            { VcZone.HiddenWhenExcludedKey, _hiddenWhenExcluded.ButtonPressed },
        };

        // List<object> (not int[]) so the values survive the JSON round-trip as a plain array.
        var included = new List<object>();
        var excluded = new List<object>();
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

        d.Add(VcZone.IncludedSeatsKey, included);
        d.Add(VcZone.ExcludedSeatsKey, excluded);

        return d;
    }

    public override void DisplayPrototype(Guid prototypeId)
    {
        DisplayPrototype(ProjectService.Instance.CurrentProject.Prototypes[prototypeId]);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        RebuildSeatList();

        var p = prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _widthInput.Text = ReadString(p, VcZone.WidthKey, "2");
        _depthInput.Text = ReadString(p, VcZone.DepthKey, "2");
        _defaultIncluded.ButtonPressed = ReadBool(p, VcZone.DefaultIncludedKey);
        _hiddenWhenExcluded.ButtonPressed = ReadBool(p, VcZone.HiddenWhenExcludedKey);

        var included = VcZone.SeatSetFor(p, VcZone.IncludedSeatsKey);
        var excluded = VcZone.SeatSetFor(p, VcZone.ExcludedSeatsKey);

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

    public override List<string> ValidateParameters(Dictionary<string, object> parameters)
    {
        var ret = new List<string>();

        if (
            !parameters.ContainsKey("ComponentName")
            || string.IsNullOrEmpty(parameters["ComponentName"].ToString())
        )
            ret.Add("Name may not be blank");

        if (Utility.GetParam<float>(parameters, VcZone.WidthKey) <= 0)
            ret.Add("Width must be > 0");
        if (Utility.GetParam<float>(parameters, VcZone.DepthKey) <= 0)
            ret.Add("Depth must be > 0");

        return ret;
    }

    private static string ReadString(Dictionary<string, object> p, string key, string def) =>
        p != null && p.TryGetValue(key, out var v) && v != null ? v.ToString() : def;

    private static bool ReadBool(Dictionary<string, object> p, string key) =>
        p != null && p.TryGetValue(key, out var v) && v is bool b && b;
}
