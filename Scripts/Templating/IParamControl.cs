using System;

namespace Lizzie;

public interface IParamControl
{
    void SetParameter(TemplateParameter parameter);

    void UpdateParameter(string newValue);
    TemplateParameter GetParameter();

    event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;
}
