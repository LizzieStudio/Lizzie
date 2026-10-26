using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using static Lizzie.TextureFactory.TextureObject;

namespace Lizzie;

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
                Value = "50",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "Y",
                Value = "50",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "Width",
                Value = "50",
            }
        );
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Number,
                Name = "Height",
                Value = "50",
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
        Parameters.Add(
            new TemplateParameter()
            {
                Type = TemplateParameter.TemplateParameterType.Boolean,
                Name = "Visible",
                Value = "True",
            }
        );
    }

    public SnowTag Id { get; set; } = SnowTag.Empty;

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

    public bool IsVisible(TextureContext context) =>
        EvaluateBooleanParameter(_parameters, "Visible", context);

    public List<ITemplateElement> Children { get; } = new();

    protected void UpdateCoreParameterData(TextureFactory.TextureObject to, TextureContext context)
    {
        to.CenterX = EvaluateFloatParameter(_parameters, "X", context);
        to.CenterY = EvaluateFloatParameter(_parameters, "Y", context);
        to.Height = EvaluateFloatParameter(_parameters, "Height", context);
        to.Width = EvaluateFloatParameter(_parameters, "Width", context);
        to.RotationDegrees = EvaluateNumberParameter(_parameters, "Rotation", context);
    }

    #region Parameter Processing

    private TemplateParameter GetParameterByName(string name) =>
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

        return Substitute(p.Value, context);
    }

    /// <summary>
    /// The number parameter <paramref name="key"/>, rounded to a whole number.
    /// </summary>
    public int EvaluateNumberParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    ) =>
        (int)
            MathF.Round(
                EvaluateFloatParameter(parameters, key, context),
                MidpointRounding.AwayFromZero
            );

    /// <summary>
    /// The number parameter <paramref name="key"/>, or 0 when it's missing or isn't a number.
    /// </summary>
    public float EvaluateFloatParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return 0;

        float.TryParse(
            Substitute(p.Value, context),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result
        );
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

        var o = Substitute(p.Value, context);

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

        var o = Substitute(p.Value, context);

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

        var s = Substitute(p.Value, context);

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

        return IsTruthy(Substitute(p.Value, context));
    }

    public static bool IsTruthy(string value) =>
        value.Trim().ToLowerInvariant() is not ("" or "0" or "false" or "f" or "no" or "n");

    public TrackElement.TrackTypeEnum EvaluateTrackParameter(
        IList<TemplateParameter> parameters,
        string key,
        TextureContext context
    )
    {
        var p = parameters.FirstOrDefault(x => x.Name == key);
        if (p == null)
            return TrackElement.TrackTypeEnum.Horizontal;

        return TrackStringToEnum(Substitute(p.Value, context));
    }

    /// <summary>
    /// Replaces each {Column} in <paramref name="text"/> with the current row's value.
    /// </summary>
    public static string Substitute(string text, TextureContext context) =>
        Regex.Replace(
            text,
            @"\{([^{}]+)\}",
            match =>
                context.Values?.GetValueOrDefault(match.Groups[1].Value) is { } value
                    ? value
                    : match.Value
        );

    #endregion
}

public interface ITemplateElement
{
    public enum TemplateElementType
    {
        Text,
        Image,
        Frame,
        Track,
    }

    TemplateElementType ElementType { get; }

    string ElementName { get; set; }

    IList<TemplateParameter> Parameters { get; }

    IList<TextureFactory.TextureObject> GetElementData(TextureContext context);

    bool IsVisible(TextureContext context);

    SnowTag Id { get; set; }

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
        Boolean,
        HorizontalAlignment,
        VerticalAlignment,
        Image,
        TrackType,
        Sequence,
    }

    public TemplateParameterType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
