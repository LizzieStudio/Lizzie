using System;
using Godot;

public partial class ComponentTemplate : Resource
{
    [Export]
    public string ComponentName;

    [Export]
    public Texture2D Icon;

    [Export]
    public string DefinitionDialogName;

    [Export]
    public string PrototypeName;

    [Export]
    public string ComponentType;

    [Export]
    public string[] PrototypeNames;
}
