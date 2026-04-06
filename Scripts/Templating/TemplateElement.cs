using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace Lizzie.Scripts.Templating;

public class TemplateElement : ITemplateElement
{
    private List<TemplateParameter> _parameters;

    // Called when the node enters the scene tree for the first time.
    protected TemplateElement()
    {
        _parameters = new List<TemplateParameter>();

        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "X",
                Value = "{HalfWidth}",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "Y",
                Value = "{HalfHeight}",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "Width",
                Value = "{Width}",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "Height",
                Value = "{Height}",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "Rotation",
                Value = "0",
            }
        );
    }

    public int Id { get; set; }
    public int Parent { get; set; } = -1; //top level

    public void SetParameterValue(string name, string value)
    {
        var p = GetParameterByName(name);
        if (p != null)
            p.Value = value;
    }

    public ITemplateElement.TemplateElementType ElementType { get; protected set; }
    public string ElementName { get; set; }
    public IList<TemplateParameter> Parameters => _parameters;

    public virtual IList<TextureFactory.TextureObject> GetElementData(TextureContext context)
    {
        return new List<TextureFactory.TextureObject>();
    }

    public List<ITemplateElement> Children { get; } = new();

    protected void UpdateCoreParameterData(TextureFactory.TextureObject to, TextureContext context)
    {
        to.CenterX = EvaluateNumberParameter(_parameters, "X", context);
        to.CenterY = EvaluateNumberParameter(_parameters, "Y", context);
        to.Anchor = EvaluateAnchorParameter(_parameters, "Anchor", context);
        to.Height = EvaluateNumberParameter(_parameters, "Height", context);
        to.Width = EvaluateNumberParameter(_parameters, "Width", context);
        to.RotationDegrees = EvaluateNumberParameter(_parameters, "Rotation", context);
    }

    public event EventHandler<EventArgs> ElementUpdated;

    protected virtual void OnElementUpdated() => ElementUpdated?.Invoke(this, EventArgs.Empty);

    #region Parameter Processing

    public TemplateParameter GetParameterByName(string name) =>
        _parameters.FirstOrDefault(x => x.Name == name);

    public string EvaluateTextParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return string.Empty;

        return ProcessKeywords(p.Value, context);
    }

    public int EvaluateNumberParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return 0;

        var o = ProcessKeywords(p.Value, context);

        _ = int.TryParse(o, out var result) ? result : 0;
        return result;
    }

    public HorizontalAlignment EvaluateHorizontalAlignmentParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return HorizontalAlignment.Left;

        var o = ProcessKeywords(p.Value, context);

        switch (o)
        {
            case "Left":
                return HorizontalAlignment.Left;
            case "Center":
                return HorizontalAlignment.Center;
            case "Right":
                return HorizontalAlignment.Right;
            default:
                return HorizontalAlignment.Left;
        }
    }

    public VerticalAlignment EvaluateHorizontaVerticalAlignment(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return VerticalAlignment.Top;

        var o = ProcessKeywords(p.Value, context);

        switch (o)
        {
            case "Top":
                return VerticalAlignment.Top;
            case "Middle":
                return VerticalAlignment.Center;
            case "Bottom":
                return VerticalAlignment.Bottom;
            default:
                return VerticalAlignment.Top;
        }
    }

    public Color EvaluateColorParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return Colors.Black;

        var s = ProcessKeywords(p.Value, context);

        return Color.FromString(s, Colors.Black);
    }

    public bool EvaluateBooleanParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return false;

        var o = ProcessKeywords(p.Value, context);

        if (string.IsNullOrWhiteSpace(o))
            return false;

        var c = o.ToUpper()[0];

        return (c == 'T' || c == 'Y' || c == '1');
    }

    public TextureFactory.TextureObject.AnchorPoint EvaluateAnchorParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return TextureFactory.TextureObject.AnchorPoint.TopLeft;

        return TextureFactory.TextureObject.AnchorStringToEnum(ProcessKeywords(p.Value, context));
    }

    public TrackElement.TrackTypeEnum EvaluateTrackParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return TrackElement.TrackTypeEnum.Horizontal;

        return TextureFactory.TextureObject.TrackStringToEnum(ProcessKeywords(p.Value, context));
    }

    private string ProcessKeywords(string parameterVal, TextureContext context)
    {
        var s = parameterVal.Replace(
            "{Width}",
            context.ParentSize.X.ToString(),
            StringComparison.InvariantCultureIgnoreCase
        );

        s = s.Replace(
            "{Height}",
            context.ParentSize.Y.ToString(),
            StringComparison.InvariantCultureIgnoreCase
        );

        s = s.Replace(
            "{HalfWidth}",
            ((int)(context.ParentSize.X / 2)).ToString(),
            StringComparison.InvariantCultureIgnoreCase
        );

        s = s.Replace(
            "{HalfHeight}",
            ((int)(context.ParentSize.Y / 2)).ToString(),
            StringComparison.InvariantCultureIgnoreCase
        );

        //check dataset to column match
        if (
            context.DataSet != null
            && !string.IsNullOrEmpty(context.CurrentRowName)
            && context.DataSet.Rows.ContainsKey(context.CurrentRowName)
        )
        {
            s = s.Replace(
                "{Name}",
                context.CurrentRowName,
                StringComparison.InvariantCultureIgnoreCase
            );

            for (int i = 0; i < context.DataSet.Columns.Count; i++)
            {
                var r = "{" + context.DataSet.Columns[i] + "}";
                s = s.Replace(r, context.DataSet.Rows[context.CurrentRowName].Data[i]);
            }
        }

        return s;
    }

    /// <summary>
    /// Retrieves all public static Color fields from the Godot.Colors class.
    /// </summary>
    private List<(string Name, Color Value)> GetAllColors()
    {
        var colorType = typeof(Colors);

        return colorType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Color))
            .Select(f => (f.Name, (Color)f.GetValue(null)!))
            .ToList();
    }

    #endregion
}

public interface ITemplateElement
{
    public enum TemplateElementType
    {
        Text,
        Box,
        Image,
        Note,
        Cirlce,
        Polygon,
        Table,
        Line,
        Container,
        Frame,
        Track,
    }

    TemplateElementType ElementType { get; }

    string ElementName { get; set; }

    IList<TemplateParameter> Parameters { get; }

    IList<TextureFactory.TextureObject> GetElementData(TextureContext context);

    int Id { get; set; }
    int Parent { get; set; }

    List<ITemplateElement> Children { get; }

    void SetParameterValue(string name, string value);
}

public class TemplateParameter
{
    public enum TemplateParameterType
    {
        Text,
        Number,
        Color,
        Anchor,
        Boolean,
        HorizontalAlignment,
        VerticalAlignment,
        Image,
        TrackType,
    }

    public TemplateParameterType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
