using Godot;

namespace Lizzie;

public partial class ComponentTemplate : Resource
{
    [Export]
    public string ComponentName;

    [Export]
    public Texture2D Icon;

    [Export]
    public string DefinitionDialogName;

    [Export]
    public string ComponentType;
}
