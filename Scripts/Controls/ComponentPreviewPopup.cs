using Godot;

namespace Lizzie;

public partial class ComponentPreviewPopup : PopupPanel
{
    private ComponentPreview _preview;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _preview = GetNode<ComponentPreview>("%ComponentPreview");
        PopupHide += QueueFree;
    }

    public void ShowComponent(VisualComponentBase component)
    {
        var prototype = Atoms.R.Get(Prototype.ById, component.PrototypeRef);
        if (prototype == null)
            return;

        _preview.Build(prototype, component.Item);
        _preview.SpinStop();
        PopupCentered();
    }
}
