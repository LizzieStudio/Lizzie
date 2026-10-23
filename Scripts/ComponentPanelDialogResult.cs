using System;
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

    /// <summary>
    /// Raised when <see cref="Submittable"/> changes.
    /// </summary>
    public event Action SubmittableChanged;

    private bool _submittable = true;

    /// <summary>
    /// Whether the parameters can be created or updated now.
    /// The dialog disables its Create and Update buttons while they can't.
    /// </summary>
    public bool Submittable
    {
        get => _submittable;
        protected set
        {
            if (_submittable == value)
                return;
            _submittable = value;
            SubmittableChanged?.Invoke();
        }
    }
}
