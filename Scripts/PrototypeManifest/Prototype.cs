using System;
using System.Text.Json.Serialization;

public class Prototype
{
    public Prototype() { }

    //unique identifier for this prototype. Should be generated when the component is created, and never changed.
    //Called "PrototypeRef" to distinguish it from the "Ref" property of VisualComponentBase, which is a reference to the visual component that represents this component in the current project.
    //(The same component may be represented by multiple visual components in different projects, but it will only have one PrototypeRef.)
    public SnowportId PrototypeRef { get; set; }
    public string Name { get; set; }

    public ComponentParameters Parameters { get; set; }

    [JsonIgnore]
    public VisualComponentBase.VisualComponentType Type => Parameters?.ComponentType ?? default;

    [JsonIgnore]
    public bool IsDirty { get; set; }

    public void Clean()
    {
        IsDirty = false;
    }
}
