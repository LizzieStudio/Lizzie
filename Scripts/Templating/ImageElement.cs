using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Lizzie.Scripts.Templating;

public class ImageElement : TemplateElement
{
    // Called when the node enters the scene tree for the first time.
    public ImageElement()
        : base()
    {
        ElementType = ITemplateElement.TemplateElementType.Image;

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Image",
                Value = "Circle",
                Type = TemplateParameter.TemplateParameterType.Image,
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
                Name = "Stretch",
                Value = "False",
                Type = TemplateParameter.TemplateParameterType.Boolean,
            }
        );
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
        t.HorizontalAlignment = EvaluateHorizontalAlignmentParameter(
            Parameters,
            "Hor Align",
            context
        );
        t.VerticalAlignment = EvaluateHorizontaVerticalAlignment(Parameters, "Ver Align", context);
        t.Text = EvaluateTextParameter(Parameters, "Image", context);
        t.ForegroundColor = EvaluateColorParameter(Parameters, "Foreground", context);
        t.Stretch = EvaluateBooleanParameter(Parameters, "Stretch", context);
        l.Add(t);
        return l;
    }
}
