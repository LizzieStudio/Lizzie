using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Lizzie.Scripts.Templating;

public class TextElement : TemplateElement
{
    // Called when the node enters the scene tree for the first time.
    public TextElement()
        : base()
    {
        ElementType = ITemplateElement.TemplateElementType.Text;

        Parameters.Add(new TemplateParameter { Name = "Text", Value = "Lipsum Orem" });

        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.HorizontalAlignment,
                Name = "Hor Align",
                Value = "Center",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.VerticalAlignment,
                Name = "Ver Align",
                Value = "Middle",
            }
        );

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Foreground",
                Value = (Colors.Black).ToHtml(),
                Type = TemplateParameter.TemplateParameterType.Color,
            }
        );
        Parameters.Add(
            new TemplateParameter
            {
                Name = "Font Size",
                Value = "12",
                Type = TemplateParameter.TemplateParameterType.Number,
            }
        );
        Parameters.Add(
            new TemplateParameter
            {
                Name = "Autosize",
                Value = "False",
                Type = TemplateParameter.TemplateParameterType.Boolean,
            }
        );
        UpdateBounds();
    }

    private void UpdateBounds()
    {
        var textSize = TextureFactory.GetTextBounds(new SystemFont(), 12, "Lipsum Orem");
        SetParameterValue("Width", textSize.X.ToString(CultureInfo.InvariantCulture));
        SetParameterValue("Height", textSize.Y.ToString(CultureInfo.InvariantCulture));
        SetParameterValue("X", "70");
        SetParameterValue("Y", "70");
    }

    public override List<TextureFactory.TextureObject> GetElementData(TextureContext context)
    {
        var l = new List<TextureFactory.TextureObject>();

        var t = new TextureFactory.TextureObject();

        UpdateCoreParameterData(t, context);
        t.Text = EvaluateTextParameter(Parameters, "Text", context);
        t.ForegroundColor = EvaluateColorParameter(Parameters, "Foreground", context);

        t.HorizontalAlignment = EvaluateHorizontalAlignmentParameter(
            Parameters,
            "Hor Align",
            context
        );
        t.VerticalAlignment = EvaluateHorizontaVerticalAlignment(Parameters, "Ver Align", context);

        var f = EvaluateNumberParameter(Parameters, "Font Size", context);
        t.FontSize = ScaleFontSize(f, context);

        t.Autosize = EvaluateBooleanParameter(Parameters, "Autosize", context);
        l.Add(t);
        return l;
    }

    private int ScaleFontSize(int fontSize, TextureContext context)
    {
        const int TwelvePointHeight = 18; //pixels in 12 point font. (Actually was 17, but I changed it to 18 to make it look better)

        float targetDots = (fontSize / 72f) * context.Dpi; //fontsize / 72 = font size in inches

        return (int)(fontSize * targetDots / TwelvePointHeight);
    }
}
