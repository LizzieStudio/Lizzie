using System.Collections.Generic;
using Godot;

namespace Lizzie;

public abstract partial class ComponentPanelDialogResult : Control
{
    public abstract ComponentParameters GetParams();

    public virtual void Activate() { }

    public virtual void Deactivate() { }

    protected float ParamToFloat(string input)
    {
        var s = input.Trim();
        if (float.TryParse(s, out var f))
        {
            return f;
        }

        return 0;
    }

    public TextureFactory TextureFactory { get; set; }

    public virtual Project CurrentProject { get; set; }

    public virtual void DisplayPrototype(Prototype prototype) { }

    public abstract List<string> ValidateParameters(ComponentParameters parameters);
}
