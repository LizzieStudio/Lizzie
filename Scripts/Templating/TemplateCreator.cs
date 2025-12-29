using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using TTSS.Scripts.Templating;

public partial class TemplateCreator : MarginContainer
{
    [Export] private TextureRect _preview;

    //private VBoxContainer _elementContainer;

    private Tree _elementTree;
    private VBoxContainer _paramContainer;

    private Button _testButton;
    
    private BoundsRect _boundsRect;

    private PackedScene _stringParam;
    private PackedScene _numberParam;
    private PackedScene _colorParam;
    private PackedScene _anchorParam;

    private ITemplateElement _selectedElement;
    private TreeItem _rootItem;

    private List<ITemplateElement> _templateElements = new();
    private TextureContext _textureContext = new();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _elementTree = GetNode<Tree>("%TemplateTree");
        _paramContainer = GetNode<VBoxContainer>("%TemplateParams");
        _testButton = GetNode<Button>("%TestButton");
        _testButton.Pressed += TestFunction;

        _boundsRect = GetNode<BoundsRect>("%BoundsRect");
        
        _stringParam = GD.Load<PackedScene>("res://Scenes/Templating/StringParam.tscn");
        _numberParam = GD.Load<PackedScene>("res://Scenes/Templating/NumericParam.tscn");
        _colorParam = GD.Load<PackedScene>("res://Scenes/Templating/ColorParam.tscn");
        _anchorParam = GD.Load<PackedScene>("res://Scenes/Templating/AnchorParam.tscn");
        
        _rootItem = _elementTree.CreateItem(); //create root item
        _elementTree.ItemSelected += TreeItemSelected;
        
        _textureContext.ParentSize = _preview.GetSize();
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
            UpdateBoundsRect();
        }
    }

    private void AddTextureElement(ITemplateElement.TemplateElementType type)
    {
        var t = new TextElement();

        var max = GetMaxId(_rootItem) + 1;

        var ni = _elementTree.CreateItem(_rootItem);
        ni.SetMetadata(0, max);
        ni.SetText(0, $"Text {max}");

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
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (t is IParamControl pc)
            {
                pc.SetParameter(p);
                pc.ParameterUpdated += UpdateTexture;
            }

            _paramContainer.AddChild(t);
        }
    }

    private void UpdateTexture(object sender, EventArgs e)
    {
        _textureContext.ParentSize = _preview.GetSize();
        
        var td = new TextureFactory.TextureDefinition
        {
            BackgroundColor = Colors.White,
            Height = 256,
            Width = 256 * 250 / 350,
            Shape = TextureFactory.TokenShape.Square
        };

        foreach (var element in _templateElements)
        {
            foreach (var l in element.GetElementData(_textureContext))
            {
                td.Objects.Add(new TextureFactory.TextureObject
                {
                    Width = td.Width,
                    Height = td.Height,
                    CenterX = l.CenterX,
                    CenterY = l.CenterY,
                    Anchor = l.Anchor,
                    Multiline = true,
                    Text = l.Text,
                    ForegroundColor = l.ForegroundColor,
                    Font = new SystemFont(),
                    Type = TextureFactory.TextureObjectType.Text
                });
            }
        }

        TextureFactory.GenerateTexture(td, UpdatePreview);
        
        UpdateBoundsRect();
    }

    private void ClearParameters()
    {
        foreach (var p in _paramContainer.GetChildren())
        {
            if (p is IParamControl pc) pc.ParameterUpdated -= UpdateTexture;
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
        
        Vector2I offset = new Vector2I(te.CenterX, te.CenterY);
        
        switch (te.Anchor)
        {
            case TextureFactory.TextureObject.AnchorPoint.TopLeft:
                offset = Vector2I.Zero;
                break;
            case TextureFactory.TextureObject.AnchorPoint.TopCenter:
                offset = new Vector2I(hw, 0);
                break;
            case TextureFactory.TextureObject.AnchorPoint.TopRight:
                offset = new Vector2I(w, 0);
                break;
            case TextureFactory.TextureObject.AnchorPoint.MiddleLeft:
                offset = new Vector2I(0, hh);
                break;
            case TextureFactory.TextureObject.AnchorPoint.MiddleCenter:
                offset = new Vector2I(hw, hh);
                break;
            case TextureFactory.TextureObject.AnchorPoint.MiddleRight:
                offset = new Vector2I(w, hh);
                break;
            case TextureFactory.TextureObject.AnchorPoint.BottomLeft:
                offset = new Vector2I(0, h);
                break;
            case TextureFactory.TextureObject.AnchorPoint.BottomCenter:
                offset = new Vector2I(hw, h);
                break;
            case TextureFactory.TextureObject.AnchorPoint.BottomRight:
                offset = new Vector2I(w, h);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        Vector2I p = new Vector2I(te.CenterX, te.CenterY) - offset;
        
        var br = new Rect2I(p, te.Width, te.Height);
        _boundsRect.SetBounds(br, _textureContext);
    }


    private void TestFunction()
    {
        AddTextureElement(ITemplateElement.TemplateElementType.Text);
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