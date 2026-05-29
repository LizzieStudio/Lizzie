using Godot;
using System;

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
        
        if (_readyNeeded) ShowComponent(_pendingComponent, _textureFactory);
	}
    
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

    public void ShowComponent(VisualComponentBase component, TextureFactory textureFactory)
    {
        if (IsNodeReady())
        {
            _readyNeeded = false;
            
            if (ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(component.PrototypeRef, out var prototype))
            {
                _preview.Build(prototype, component.DataSetRow, textureFactory);
                _preview.SpinStop();
                Show();
            }
        }
        else
        {
            _readyNeeded = true;
            _pendingComponent = component;
            _textureFactory = textureFactory;
        }
    }

    private bool _readyNeeded;
    private VisualComponentBase _pendingComponent;
    private TextureFactory _textureFactory;

    public event EventHandler<EventArgs> CloseDialog;

    private void OnCloseClick()
    {
        CloseDialog?.Invoke(this, EventArgs.Empty);
    }
}
