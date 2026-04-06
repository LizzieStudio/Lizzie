using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public class Prototype
{
    public Prototype() { }

    public Prototype(PrototypeDto dto)
    {
        PrototypeRef = dto.PrototypeRef;
        Name = dto.Name;
        Type = dto.Type;

        var d = JsonSerializer.Deserialize<Dictionary<string, object>>(dto.JsonParameters);

        Parameters = JsonUtilities.ParseJsonToDictionary(Type, d);
    }

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

/// <summary>
/// This class is used for serializing and deserializing prototypes to and from JSON.
/// </summary>
public class PrototypeDto
{
    public PrototypeDto(Prototype prototype)
    {
        PrototypeRef = prototype.PrototypeRef;
        Name = prototype.Name;
        Type = prototype.Type;
        JsonParameters = JsonSerializer.Serialize(prototype.Parameters);
    }

    /// <summary>
    /// Need parameterless constructor for JSON deserialization. The properties will be set manually after deserialization.
    /// </summary>
    public PrototypeDto() { }

    public Guid PrototypeRef { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// This property is the string representation of the Parameters dictionary in Prototype. It is used for JSON serialization and deserialization, because Dictionary<string, object> cannot be directly serialized to JSON.
    /// It needs to be unpacked with JsonUtilities.
    /// </summary>
    public string JsonParameters { get; set; }
    public VisualComponentBase.VisualComponentType Type { get; set; }
}
