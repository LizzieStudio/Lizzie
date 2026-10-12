using System.Collections.Generic;
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

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Quantity",
                Value = "1",
                Type = TemplateParameter.TemplateParameterType.Number,
            }
        );
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
        t.Quantity = EvaluateNumberParameter(Parameters, "Quantity", context);
        l.Add(t);
        return l;
    }
}
