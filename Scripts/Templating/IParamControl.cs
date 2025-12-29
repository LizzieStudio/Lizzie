using Godot;
using System;
using TTSS.Scripts.Templating;

public interface IParamControl
{
    void SetParameter(TemplateParameter parameter);
    TemplateParameter GetParameter();
    
    event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;
}
