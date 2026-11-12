using Godot;

namespace Lizzie;

/// <summary>
/// A field for a <see cref="SequenceExpressions"/> input. It tints its text when the input doesn't parse.
/// </summary>
public partial class SequenceEdit : LineEdit
{
    public enum ValueKind
    {
        Numbers,
        Strings,
    }

    /// <summary>
    /// Which parser checks the input.
    /// </summary>
    [Export]
    public ValueKind Kind { get; set; }

    [Export(PropertyHint.MultilineText)]
    public string Syntax { get; set; } = "";

    [Export]
    public Color ErrorColor { get; set; } = new(1, 0.4f, 0.4f);

    private string _checked;

    public override string _GetTooltip(Vector2 atPosition) =>
        string.IsNullOrEmpty(TooltipText) ? Syntax : $"{TooltipText}\n\n{Syntax}";

    public override void _Draw()
    {
        if (Text != _checked)
            Callable.From(Mark).CallDeferred();
    }

    private void Mark()
    {
        _checked = Text;
        var valid =
            Kind == ValueKind.Numbers
                ? SequenceExpressions.TryParseNumbers(Text, out _)
                : SequenceExpressions.TryParseStrings(Text, out _);
        if (valid)
            RemoveThemeColorOverride("font_color");
        else
            AddThemeColorOverride("font_color", ErrorColor);
    }
}
