using System;
using System.Collections.Generic;
using Godot;

public class Prototype
{
    //unique identifier for this prototype. Should be generated when the component is created, and never changed.
    //Called "PrototypeRef" to distinguish it from the "Ref" property of VisualComponentBase, which is a reference to the visual component that represents this component in the current project.
    //(The same component may be represented by multiple visual components in different projects, but it will only have one PrototypeRef.)
    public Guid PrototypeRef { get; set; }
    public string Name { get; set; }
    public Dictionary<string, object> Parameters { get; set; }

    public VisualComponentBase.VisualComponentType Type { get; set; }

    public bool IsDirty { get; set; }

    public void Clean()
    {
        IsDirty = false;
    }
}
