using System;
using Godot;

public partial class ComponentPreviewPopup : Window
{
    private ComponentPreview _preview;
    private Button _closeButton;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _preview = GetNode<ComponentPreview>("%ComponentPreview");
        _closeButton = GetNode<Button>("%CloseButton");
        _closeButton.Pressed += OnCloseClick;
        CloseRequested += OnCloseClick;
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }

    public void ShowComponent(VisualComponentBase component, TextureFactory textureFactory)
    {
        var prototype = ProjectService.Instance.GetIncludingDeleted<Prototype>(
            component.PrototypeRef
        );
        if (prototype == null)
            return;

        _preview.Build(
            prototype,
            component.DataSetRowIndex,
            component.DataSetRowId,
            textureFactory
        );
        _preview.SpinStop();
        Show();
    }

    public event EventHandler<EventArgs> CloseDialog;

    private void OnCloseClick()
    {
        CloseDialog?.Invoke(this, EventArgs.Empty);
    }
}
