using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Lizzie.Scripts.Templating;

public class FrameElement : TemplateElement
{
    // Called when the node enters the scene tree for the first time.
    public FrameElement()
        : base()
    {
        ElementType = ITemplateElement.TemplateElementType.Frame;

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Stroke Color",
                Value = (Colors.Black).ToHtml(),
                Type = TemplateParameter.TemplateParameterType.Color,
            }
        );
        Parameters.Add(
            new TemplateParameter
            {
                Name = "Stroke Width",
                Value = "2",
                Type = TemplateParameter.TemplateParameterType.Number,
            }
        );

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Background Color",
                Value = (Colors.Transparent).ToHtml(),
                Type = TemplateParameter.TemplateParameterType.Color,
            }
        );

        UpdateBounds();
    }

    private void UpdateBounds()
    {
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
        t.Type = TextureFactory.TextureObjectType.RectangleFrame;
        t.ForegroundColor = EvaluateColorParameter(Parameters, "Stroke Color", context);
        t.BackgroundColor = EvaluateColorParameter(Parameters, "Background Color", context);
        t.FontSize = EvaluateNumberParameter(Parameters, "Stroke Width", context);
        l.Add(t);
        return l;
    }
}
