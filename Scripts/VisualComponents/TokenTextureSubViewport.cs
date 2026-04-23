using System;
using System.Collections.Generic;
using System.Data;
using Godot;

public partial class TokenTextureSubViewport : SubViewport
{
    [Export]
    private string[] _shapes;

    private List<Texture2D> _shapeTextures;

    private ColorRect _square;
    private Label _label;
    private TextureRect _hexPoint;
    private TextureRect _hexFlat;
    private TextureRect _circle;
    private LabelSettings _labelSettings;

    private TextureRect _clipRect;
    private TextureRect _textureRect;

    private Color _bgColor = Colors.White;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        //_square = GetNode<ColorRect>("ColorRect");

        _label = GetNode<Label>("Label");
        _labelSettings = new LabelSettings();
        _labelSettings.FontSize = 24;
        _labelSettings.FontColor = Colors.Black;
        _label.LabelSettings = _labelSettings;

        _clipRect = GetNode<TextureRect>("ClipRect");
        _textureRect = GetNode<TextureRect>("ClipRect/TextureRect");

        LoadShapeTextures();
        SetShape(TokenShape.Square);
    }

    private void LoadShapeTextures()
    {
        _shapeTextures = new List<Texture2D>();
        foreach (var s in _shapes)
        {
            _shapeTextures.Add(LoadTexture(s));
        }
    }

    private Texture2D LoadTexture(string filename)
    {
        //var image = Image.LoadFromFile(filename);

        var t2 = (Texture2D)GD.Load(filename);
        //var texture = new ImageTexture();
        //texture.SetImage(image);
        return t2;

        return new ImageTexture();
    }

    public Texture2D CreateQuickTexture(TokenTextureParameters parameters)
    {
        SetSize(parameters.Width, parameters.Height);
        SetShape(parameters.Shape);
        SetBackgroundColor(parameters.BackgroundColor);
        SetText(parameters.Caption);
        SetTextColor(parameters.CaptionColor);
        SetFontSize(parameters.FontSize);

        return GetTexture();
    }

    public void SetBackgroundColor(Color color)
    {
        _clipRect.Modulate = color;
        _bgColor = color;
    }

    public void SetText(string text)
    {
        _label.Text = text;
    }

    public void SetTextColor(Color color)
    {
        _label.LabelSettings.FontColor = color;
    }

    /// <summary>
    /// The number assignments here need to stay the same.
    /// This is cast to an int in various places
    /// </summary>
    public enum TokenShape
    {
        Square = 0,
        Circle = 1,
        HexPoint = 2,
        HexFlat = 3,
        RoundedRect = 4,
    }

    public void SetShape(TokenShape shape)
    {
        _clipRect.Texture = _shapeTextures[(int)shape];

        /*
        _square.Visible = (shape == TokenShape.Square);
        _circle.Visible = (shape == TokenShape.Circle);
        _hexPoint.Visible = (shape == TokenShape.HexPoint);
        _hexFlat.Visible = (shape == TokenShape.HexFlat);
        */
    }

    public void SetSize(float width, float height)
    {
        if (width == 0 || height == 0)
            return;
        //find min - scale that to 128 pixels
        var max = Math.Max(width, height);
        float scale = 128f / max;

        var size = new Vector2I((int)(width * scale), (int)(height * scale));

        Size = size;
        Size2DOverride = size;
    }

    public void SetTexture(ImageTexture texture)
    {
        var imageSize = texture.GetSize();
        var viewportSize = new Vector2I((int)imageSize.X, (int)imageSize.Y);
        Size = viewportSize;
        Size2DOverride = viewportSize;
        _textureRect.Texture = texture;
    }

    public void SetFontSize(int fontSize)
    {
        if (fontSize > 0)
            _labelSettings.FontSize = fontSize;
    }

    public enum ShapeViewportMode
    {
        Shape,
        Texture,
    }

    public void SetViewPortMode(ShapeViewportMode mode)
    {
        switch (mode)
        {
            case ShapeViewportMode.Shape:
                _clipRect.ClipChildren = CanvasItem.ClipChildrenMode.Disabled;
                _clipRect.Modulate = _bgColor;
                _label.Visible = true;
                _textureRect.Visible = false;
                break;

            case ShapeViewportMode.Texture:
                _clipRect.ClipChildren = CanvasItem.ClipChildrenMode.Only;
                _clipRect.Modulate = Colors.White;
                _textureRect.Visible = true;
                break;
        }
    }
}
