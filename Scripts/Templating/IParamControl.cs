using System;
using Godot;
using Lizzie.Scripts.Templating;

public interface IParamControl
{
    void SetParameter(TemplateParameter parameter);

    void UpdateParameter(string newValue);
    TemplateParameter GetParameter();

    event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;
}
