using System;
using Godot;

/// <summary>
/// Edits one <see cref="QuickSuit"/>.
/// </summary>
public partial class QuickSuitRow : HBoxContainer
{
    [Export]
    private ColorPickerButton _background;

    [Export]
    private ColorPickerButton _foreground;

    [Export]
    private SequenceEdit _values;

    [Export]
    private OptionButton _icon;

    [Export]
    private CheckBox _showText;

    public event Action Changed;

    private static readonly IconLibrary Icons = new();

    private string _iconName = "";

    public override void _EnterTree()
    {
        RecordService.Instance.Watch(this, Sync);
    }

    public override void _Ready()
    {
        _background.ColorChanged += _ => Changed?.Invoke();
        _foreground.ColorChanged += _ => Changed?.Invoke();
        _values.TextChanged += _ => Changed?.Invoke();
        _icon.ItemSelected += index =>
        {
            _iconName = IconAt((int)index);
            Changed?.Invoke();
        };
        _showText.Toggled += _ => Changed?.Invoke();
    }

    // The icons include the project's images, so the list follows them.
    private void Sync(IRecordReader R)
    {
        _icon.Clear();
        _icon.AddItem("None");
        Icons.LoadOptionButton(_icon, R);
        SelectIcon();
    }

    private void SelectIcon()
    {
        for (int i = 0; i < _icon.ItemCount; i++)
        {
            if (IconAt(i) == _iconName)
            {
                _icon.Select(i);
                return;
            }
        }
        _icon.Select(0);
    }

    // The first item, "None", stands for no icon.
    private string IconAt(int index) => index == 0 ? "" : _icon.GetItemText(index);

    public QuickSuit Suit
    {
        get =>
            new()
            {
                Background = _background.Color,
                Foreground = _foreground.Color,
                Values = _values.Text,
                Icon = _iconName,
                ShowText = _showText.ButtonPressed,
            };
        set
        {
            _background.Color = value.Background;
            _foreground.Color = value.Foreground;
            _values.Text = value.Values;
            _iconName = value.Icon;
            SelectIcon();
            _showText.SetPressedNoSignal(value.ShowText);
        }
    }
}
