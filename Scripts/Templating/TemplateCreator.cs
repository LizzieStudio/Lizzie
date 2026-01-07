using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using TTSS.Scripts.Templating;

public partial class TemplateCreator : MarginContainer
{
    [Export] private TextureRect _preview;

    //private VBoxContainer _elementContainer;

    private Tree _elementTree;
    private VBoxContainer _paramContainer;

    private Button _textButton;
    private Button _closeButton;
    private Button _imageButton;
    
    private BoundsRect _boundsRect;

    private PackedScene _stringParam;
    private PackedScene _numberParam;
    private PackedScene _colorParam;
    private PackedScene _anchorParam;
    private PackedScene _boolParam;
    private PackedScene _horJustifyParam;
    private PackedScene _verJustifyParam;

    private ITemplateElement _selectedElement;
    private TreeItem _rootItem;

    private List<ITemplateElement> _templateElements = new();
    private TextureContext _textureContext = new();

    private Timer _updateTimer;
    private bool _updateRequired;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _elementTree = GetNode<Tree>("%TemplateTree");
        _paramContainer = GetNode<VBoxContainer>("%TemplateParams");
        _textButton = GetNode<Button>("%TextButton");
        _textButton.Pressed += AddText;
        
        _imageButton = GetNode<Button>("%ImageButton");
        _imageButton.Pressed += AddImage;

        _closeButton = GetNode<Button>("%CloseButton");
        _closeButton.Pressed += Hide;
        
        _boundsRect = GetNode<BoundsRect>("%BoundsRect");
        _boundsRect.Hide();
        _boundsRect.BoundsChanged += BoundsChanged;
        
        _stringParam = GD.Load<PackedScene>("res://Scenes/Templating/StringParam.tscn");
        _numberParam = GD.Load<PackedScene>("res://Scenes/Templating/NumericParam.tscn");
        _colorParam = GD.Load<PackedScene>("res://Scenes/Templating/ColorParam.tscn");
        _anchorParam = GD.Load<PackedScene>("res://Scenes/Templating/AnchorParam.tscn");
        _boolParam = GD.Load<PackedScene>("res://Scenes/Templating/BooleanParam.tscn");
        _horJustifyParam = GD.Load<PackedScene>("res://Scenes/Templating/HorJustifyParam.tscn");
        _verJustifyParam = GD.Load<PackedScene>("res://Scenes/Templating/VerJustifyParam.tscn");
        
        _rootItem = _elementTree.CreateItem(); //create root item
        _elementTree.ItemSelected += TreeItemSelected;
        
        _textureContext.ParentSize = _preview.GetSize();

        _updateTimer = GetNode<Timer>("Timer");
        _updateTimer.Timeout += UpdateTimerExpired;
        _updateTimer.Start();
    }

    private void UpdateTimerExpired()
    {
        if (_updateRequired)
        {
            _updateRequired = false;
            UpdateTexture(false);
        }
    }

    private void BoundsChanged(object sender, EventArgs e)
    {
        if (_selectedElement == null) return;
    
        var m = _boundsRect.GetBounds();

        int w = (int)_textureContext.ParentSize.X - m.l - m.r;
        int h = (int)_textureContext.ParentSize.Y - m.t - m.b;
            
        UpdateParamControl("X", (m.l + w/2).ToString(CultureInfo.InvariantCulture));
        UpdateParamControl("Y", (m.t+ h/2).ToString(CultureInfo.InvariantCulture));
        UpdateParamControl("Width", w.ToString(CultureInfo.InvariantCulture));
        UpdateParamControl("Height", h.ToString(CultureInfo.InvariantCulture));

        _updateRequired = true;
    }

    private void UpdateParamControl(string name, string value)
    {
        var p = GetParamControl(name);
        if (p != null) p.UpdateParameter(value);
    }

    private IParamControl GetParamControl(string name)
    {
        foreach (var node in _paramContainer.GetChildren())
        {
            if (node is IParamControl pc && pc.GetParameter().Name == name)
            {
                return pc;
            }
        }

        return null;
    }
    
    

    private void TreeItemSelected()
    {
        //get the Id
        var id = _elementTree.GetSelected().GetMetadata(0).AsInt32();

        //matching param
        var p = _templateElements.FirstOrDefault(x => x.Id == id);
        if (p != null)
        {
            _selectedElement = p;
            RemapParameters();
            _boundsRect.Show();
            UpdateBoundsRect();
        }
    }

    private void AddTextureElement(ITemplateElement.TemplateElementType type)
    {
        var max = GetMaxId(_rootItem) + 1;

        TemplateElement t;
        string prefix;

        if (type == ITemplateElement.TemplateElementType.Image)
        {
            t = new ImageElement();
            prefix = "Image";
        }
        else
        {
            t = new TextElement();
            prefix = "Text";
        }
        
        
        var ni = _elementTree.CreateItem(_rootItem);
        ni.SetMetadata(0, max);
        ni.SetText(0, $"{prefix} {max}");

        t.Id = max;

        _templateElements.Add(t);

        _elementTree.SetSelected(ni, 0);
    }

    /// <summary>
    /// Recursively iterates through all TreeItems starting from a given item.
    /// </summary>
    private int GetMaxId(TreeItem item)
    {
        int maxId = 0;

        if (item == null)
            return 0;

        var id = item.GetMetadata(0).AsInt32();
        maxId = Math.Max(maxId, id);

        // Iterate over children
        TreeItem child = item.GetFirstChild();
        while (child != null)
        {
            maxId = Math.Max(GetMaxId(child), maxId); // Recursive call
            child = child.GetNext();
        }

        return maxId;
    }

    private void RemapParameters()
    {
        if (_selectedElement == null) return;

        ClearParameters();

        foreach (var p in _selectedElement.Parameters)
        {
            HBoxContainer t;

            switch (p.Type)
            {
                case TemplateParameter.TemplateParameterType.Text:
                    t = _stringParam.Instantiate<NumericParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Number:
                    t = _numberParam.Instantiate<NumericParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Color:
                    t = _colorParam.Instantiate<ColorParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Anchor:
                    t = _anchorParam.Instantiate<ListParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Boolean:
                    t = _boolParam.Instantiate<BooleanParamControl>();
                    break;
                
                case TemplateParameter.TemplateParameterType.HorizontalAlignment:
                    t = _horJustifyParam.Instantiate<ListParamControl>();
                    break;
                
                case TemplateParameter.TemplateParameterType.VerticalAlignment:
                    t = _verJustifyParam.Instantiate<ListParamControl>();
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (t is IParamControl pc)
            {
                pc.SetParameter(p);
                pc.ParameterUpdated += OnTextureUpdate;
            }

            _paramContainer.AddChild(t);
        }
    }

    private void OnTextureUpdate(object sender, EventArgs e)
    {
       UpdateTexture(true);
    }

    private void UpdateTexture(bool updateBounds)
    {
        _textureContext.ParentSize = _preview.GetSize();
        
        var td = new TextureFactory.TextureDefinition
        {
            BackgroundColor = Colors.White,
            Height = (int)_textureContext.ParentSize.Y,
            Width = (int) _textureContext.ParentSize.X,
            Shape = TextureFactory.TokenShape.Square
        };

        foreach (var element in _templateElements)
        {
            foreach (var l in element.GetElementData(_textureContext))
            {
                td.Objects.Add(new TextureFactory.TextureObject
                {
                    Width = l.Width,
                    Height = l.Height,
                    CenterX = l.CenterX,
                    CenterY = l.CenterY,
                    Anchor = l.Anchor,
                    Multiline = true,
                    Text = l.Text,
                    ForegroundColor = l.ForegroundColor,
                    Font = new SystemFont(),
                    FontSize = l.FontSize,
                    Autosize = l.Autosize,
                    HorizontalAlignment = l.HorizontalAlignment,
                    VerticalAlignment = l.VerticalAlignment,
                    Type = l.Type
                });
            }
        }

        TextureFactory.GenerateTexture(td, UpdatePreview);
        
        if (updateBounds) UpdateBoundsRect();
    }

    private void ClearParameters()
    {
        foreach (var p in _paramContainer.GetChildren())
        {
            if (p is IParamControl pc) pc.ParameterUpdated -= OnTextureUpdate;
            p.QueueFree();
        }
    }

    private void UpdateBoundsRect()
    {
        if (_selectedElement == null)
        {
            _boundsRect.Hide();
            return;
        }
        
        _boundsRect.Show();
        
        var d = _selectedElement.GetElementData(_textureContext);
        
        //parent bounds are always in node 0;
        if (!d.Any()) return;
        
        var te = d[0];

        int h = te.Height;
        int w = te.Width;
        int hh = (int)te.Height / 2;
        int hw = (int)te.Width / 2;
        
     
        
        Vector2I p = new Vector2I(te.CenterX, te.CenterY);
        
        var br = new Rect2I(p, te.Width, te.Height);
        _boundsRect.SetBounds(br, _textureContext);
    }


    private void AddText()
    {
        AddTextureElement(ITemplateElement.TemplateElementType.Text);
        UpdateTexture(true);
    }

    private void AddImage()
    {
        AddTextureElement(ITemplateElement.TemplateElementType.Image);
        UpdateTexture(true);
    }

    private void UpdatePreview(ImageTexture texture)
    {
        _preview.Texture = texture;
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    public TextureFactory TextureFactory { get; set; }
}

public class TextureContext()
{
    public Vector2 ParentSize { get; set; }
}