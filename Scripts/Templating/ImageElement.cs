using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace TTSS.Scripts.Templating;

public class ImageElement : TemplateElement
{
    // Called when the node enters the scene tree for the first time.
    public ImageElement() : base()
    {
        ElementType = ITemplateElement.TemplateElementType.Text;

        Parameters.Add(new TemplateParameter { Name = "Name", Value = "Circle", Type = TemplateParameter.TemplateParameterType.Image });
        Parameters.Add(new TemplateParameter
        {
            Name = "Foreground",
            Value = (Colors.Black).ToHtml(),
            Type = TemplateParameter.TemplateParameterType.Color
        });

        Parameters.Add(new TemplateParameter
            { Name = "Stretch", Value = "False", Type = TemplateParameter.TemplateParameterType.Boolean });
        UpdateBounds();
    }

    private void UpdateBounds()
    {
        var textSize = TextureFactory.GetTextBounds(new SystemFont(), 12, "Lipsum Orem");
        SetParameterValue("Width", "100");
        SetParameterValue("Height", "100");
        SetParameterValue("X", "70");
        SetParameterValue("Y", "70");
    }

    public override List<TextureFactory.TextureObject> GetElementData(TextureContext context)
    {
        var l = new List<TextureFactory.TextureObject>();

        var t = new TextureFactory.TextureObject();

        UpdateCoreParameterData(t, context);
        t.Type = TextureFactory.TextureObjectType.CoreShape;
        t.Text = EvaluateTextParameter(Parameters, "Name", context);
        t.ForegroundColor = EvaluateColorParameter(Parameters, "Foreground", context);
        t.Stretch = EvaluateBooleanParameter(Parameters, "Stretch", context);
        l.Add(t);
        return l;
    }

    private static int ForceParse(string s)
    {
        if (int.TryParse(s, out var i)) return i;
        return 0;
    }
}

