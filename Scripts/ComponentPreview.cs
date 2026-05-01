using System;
using System.Collections.Generic;
using Godot;

public partial class ComponentPreview : Panel
{
    private Node3D _parentNode;
    private Label _previewLabel;

    private Button _spinButton;
    private Button _frontView;
    private Button _backView;

    private Button _zoomIn;
    private Button _zoomOut;
    private Button _zoomToFit;

    private SubViewportContainer _subViewportContainer;
    private SubViewport _subViewport;

    private PageControl _pageControl;
    private Camera3D _camera;

    public override void _Ready()
    {
        _parentNode = GetNode<Node3D>("%Node3D");
        _previewLabel = GetNode<Label>("%PreviewLabel");
        _pageControl = GetNode<PageControl>("%PageControl");
        _pageControl.ItemSelected += ChangePage;
        _pageControl.Visible = false;
        _pageControl.SetItemCount(ItemCount);

        _spinButton = GetNode<Button>("%SpinButton");

        _frontView = GetNode<Button>("%FrontView");
        _frontView.Pressed += () => ShowView(0);

        _backView = GetNode<Button>("%BackView");
        _backView.Pressed += () => ShowView(180);

        _subViewportContainer = GetNode<SubViewportContainer>("%SubViewportContainer");
        _subViewportContainer.MouseTarget = true;
        _subViewportContainer.MouseFilter = Control.MouseFilterEnum.Pass;

        _subViewport = GetNode<SubViewport>("%SubViewport");
        //_subViewport.Size = new Vector2I((int)Size.X, (int)_subViewportContainer.Size.Y);

        _zoomIn = GetNode<Button>("%ZoomIn");
        _zoomIn.Pressed += ZoomIn;

        _zoomOut = GetNode<Button>("%ZoomOut");
        _zoomOut.Pressed += ZoomOut;

        _zoomToFit = GetNode<Button>("%ZoomFit");
        _zoomToFit.Pressed += () => AutoZoomComponent(_component);

        _camera = _subViewport.GetCamera3D();
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible)
            return;

        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomIn();
                GetViewport().SetInputAsHandled();
            }
            else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomOut();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void ZoomIn()
    {
        _camera.Size *= 0.8f;
    }

    private void ZoomOut()
    {
        _camera.Size *= 1.25f;
    }

    public override void _Process(double delta)
    {
        if (_component != null && _spinButton.ButtonPressed)
        {
            _component.Rotation += new Vector3(0, (float)delta, 0);
        }

        if (_buildNeeded && _component != null && _component.IsNodeReady())
        {
            Build(_component.Parameters, _row, _textureFactory);
            _buildNeeded = false;
        }
    }

    private bool _zoomInNeeded;
    private bool _zoomOutNeeded;

    private TextureFactory _textureFactory;

    private VisualComponentBase _component;
    private bool _buildNeeded;
    private string _row;

    private bool _componentActive;

    public void SetComponent(VisualComponentBase component, Vector3 rotation)
    {
        if (_componentActive)
        {
            ClearComponent();
        }

        _component = component;
        _componentActive = true;
        _component.Rotation = rotation;
        _parentNode.AddChild(_component);
        AutoZoomComponent(_component);
    }

    public VisualComponentBase GetComponent()
    {
        return _component;
    }

    public void ClearComponent()
    {
        if (_component == null)
            return;
        _component.QueueFree();
        _component = null;
        _componentActive = false;
    }

    public void SetComponentVisibility(bool visibility)
    {
        if (_component == null)
            return;
        _component.Visible = visibility;
    }

    public void SetComponentX(float x)
    {
        var c = _subViewport.GetChildren();

        var a = c?[0] as Node3D;
        if (a == null)
            return;

        a.Position = new Vector3(x, a.Position.Y, a.Position.Z);
    }

    protected void AutoZoomComponent(VisualComponentBase component)
    {
        if (component == null)
            return;

        var aabb = new Aabb();
        bool hasAabb = false;

        foreach (var child in component.GetChildren())
        {
            if (child is VisualInstance3D visualInstance)
            {
                var childAabb = visualInstance.GetAabb();
                if (!hasAabb)
                {
                    aabb = childAabb;
                    hasAabb = true;
                }
                else
                {
                    aabb = aabb.Merge(childAabb);
                }
            }
        }

        if (hasAabb)
        {
            var size = aabb.Size;
            var maxSize = size.Length();
            _camera.Size = maxSize * 1.2f;
            _camera.Position = new Vector3(0, 0, Mathf.Max(100f, maxSize + 1f));
        }
        else
        {
            _camera.Size = 5.0f;
        }
    }

    public void Build(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        Build(parameters, string.Empty, textureFactory);
    }

    public void Build(
        Dictionary<string, object> parameters,
        string row,
        TextureFactory textureFactory
    )
    {
        if (_component != null)
        {
            if (string.IsNullOrWhiteSpace(row))
            {
                //we are doing this because not all components override the Build method with the row parameter, and we don't want to break those that don't
                _component.Setup(parameters, textureFactory);
                _component.Build();
            }
            else
            {
                _component.Setup(parameters, row, textureFactory);
                _component.Build();
            }
        }
    }

    public void Build(Prototype prototype, TextureFactory textureFactory)
    {
        Build(prototype, string.Empty, textureFactory);
    }

    public void Build(Prototype prototype, string row, TextureFactory textureFactory)
    {
        var c = SpawnComponent(prototype);
        _row = row;
        _buildNeeded = true;

        SetComponent(c, GetRotationVector(prototype.Type));
        _textureFactory = textureFactory;
    }

    private VisualComponentBase SpawnComponent(Prototype prototype)
    {
        var s = Utility.ComponentTypeToScenePath(prototype.Type, prototype.Parameters);
        var scene = GD.Load<PackedScene>(s);
        var c = scene.Instantiate<VisualComponentBase>();
        c.PrototypeRef = prototype.PrototypeRef;
        return c;
    }

    private Vector3 GetRotationVector(VisualComponentBase.VisualComponentType componentType)
    {
        Vector3 v = componentType switch
        {
            VisualComponentBase.VisualComponentType.Cube => new Vector3(Mathf.DegToRad(-10), 0, 0),
            VisualComponentBase.VisualComponentType.Disc => new Vector3(Mathf.DegToRad(-10), 0, 0),
            VisualComponentBase.VisualComponentType.Token => new Vector3(Mathf.DegToRad(90), 0, 0),
            VisualComponentBase.VisualComponentType.Deck => new Vector3(Mathf.DegToRad(90), 0, 0),
            VisualComponentBase.VisualComponentType.Die => new Vector3(Mathf.DegToRad(-45), 0, 0),
            VisualComponentBase.VisualComponentType.Mesh => new Vector3(Mathf.DegToRad(-10), 0, 0),
            VisualComponentBase.VisualComponentType.Meeple => new Vector3(
                Mathf.DegToRad(-10),
                0,
                0
            ),
            _ => Vector3.Zero,
        };
        return v;
    }

    #region Multi-preview mode

    private bool _multiItemMode;

    public bool MultiItemMode
    {
        get => _multiItemMode;
        set
        {
            _multiItemMode = value;
            _previewLabel.Visible = !value;
            _pageControl.Visible = value;
        }
    }

    private void ChangePage(object sender, ItemSelectedEventArgs e)
    {
        CurrentItem = _pageControl.GetCurrentItem();
        ItemSelected?.Invoke(this, new ItemSelectedEventArgs(CurrentItem));
    }

    private int _itemCount = 1;

    public int ItemCount
    {
        get => _itemCount;
        set
        {
            if (value < 1)
            {
                _itemCount = 1;
            }
            else
            {
                _itemCount = value;
            }
            _pageControl.SetItemCount(_itemCount);
        }
    }

    public void SetItemLabels(IList<string> labels)
    {
        _pageControl.SetItemLabels(labels);
    }

    public int CurrentItem { get; set; }

    public event EventHandler<ItemSelectedEventArgs> ItemSelected;

    #endregion

    #region Viewing controls

    private void ShowView(float angle)
    {
        _spinButton.ButtonPressed = false; //stop spinning

        if (_component == null)
            return;
        var r = new Vector3(_component.Rotation.X, Mathf.DegToRad(angle), _component.Rotation.Z);
        _component.Rotation = r;
    }

    #endregion
}

public class ItemSelectedEventArgs : EventArgs
{
    public ItemSelectedEventArgs(int index)
    {
        Index = index;
    }

    public ItemSelectedEventArgs(int index, string caption)
    {
        Index = index;
        Caption = caption;
    }

    public int Index { get; set; }
    public string Caption { get; set; }
}
