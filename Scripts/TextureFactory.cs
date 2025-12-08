using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

public partial class TextureFactory : SubViewport
{
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _viewport = this;
    }

    private int _frameCount;
    private bool _generated;

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        if (_viewportUpdating > 0)
        {
            _viewportUpdating--;
            if (_viewportUpdating == 0)
            {
                //We have to do this in waves because there is a weird bug in Godot (at least in 4.2)
                //where if too many labels are rendered at once the later ones get skipped
                if (_skip * Take >= _textureDefinition.Objects.Count)
                {
                    AfterRender();
                }
                else
                {
                    GenerateSecondaryTexture();
                }
            }
        }
    }

    private void TextureDone(ImageTexture obj)
    {
        var d = obj.GetImage();
        d.SavePng(@"c:\winwam5\tfTest.png");
    }
    
    
    SubViewport _viewport;
    private int _viewportUpdating;
    private Action<ImageTexture> _onReady;
    private TextureDefinition _textureDefinition;
    private int _skip;
    private const int Take = 10;
    
    /// <summary>
    /// Generate a texture
    /// </summary>
    public void GenerateTexture(
        TextureDefinition definition,
        Action<ImageTexture> textureReadyCallback)
    {
        _onReady = textureReadyCallback;
        _textureDefinition = definition;
        
        // For drawing, we need to render to a texture using a viewport
        // Create a SubViewport for rendering
        _viewport.Size = new Vector2I(definition.Width, definition.Height);
        _viewport.RenderTargetClearMode = SubViewport.ClearMode.Always;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _viewport.TransparentBg = true;
      
        // Create a ColorRect for the background
        var bgRect = new ColorRect();
        bgRect.Color = definition.BackgroundColor;
        bgRect.Size = new Vector2(definition.Width, definition.Height);
        _viewport.AddChild(bgRect);

        
        
        foreach (var obj in definition.Objects.Take(Take))
        {
            switch (obj.Type)
            {
                case TextureObjectType.RectangleText:
                    RenderRectangleText(obj);
                    break;
                case TextureObjectType.TriangleText:
                    RenderTriangleText(obj);
                    break;
                case TextureObjectType.RectangleShape:
                    break;
                case TextureObjectType.CircleShape:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
        }
        
        
        // Get the rendered texture
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        
        _viewportUpdating = 2;
        _skip = 1;
    }

    private void GenerateSecondaryTexture()
    {
        //cleanup
        foreach(var c in _viewport.GetChildren()) c.QueueFree();
        
        // Create a ColorRect for the background
        var bgRect = new ColorRect();
        bgRect.Color = _textureDefinition.BackgroundColor;
        bgRect.Size = new Vector2(_textureDefinition.Width, _textureDefinition.Height);
        //_viewport.AddChild(bgRect);
        
        var tr = new TextureRect();
        var texture = _viewport.GetTexture();
        var image = texture.GetImage();
        tr.Size = new Vector2(_textureDefinition.Width, _textureDefinition.Height);
        tr.Texture = ImageTexture.CreateFromImage(image);
        _viewport.AddChild(tr);

        foreach (var obj in _textureDefinition.Objects.Skip(_skip * Take).Take(Take))
        {
            switch (obj.Type)
            {
                case TextureObjectType.RectangleText:
                    RenderRectangleText(obj);
                    break;
                case TextureObjectType.TriangleText:
                    RenderTriangleText(obj);
                    break;
                case TextureObjectType.RectangleShape:
                    break;
                case TextureObjectType.CircleShape:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
        }
        
        
        // Get the rendered texture
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        
        _viewportUpdating = 2;
        _skip++;
    }

    private void RenderRectangleText(TextureObject obj)
    {
        // Get text size for centering
        
        int fontSize = AutosizeFont(obj.Text, obj.Font, obj.Height, obj.Width, 6, 72);
        Vector2 textSize = obj.Font.GetStringSize(obj.Text, fontSize: fontSize);

        // Calculate the position to center the text
        float halfWidth = textSize.X / 2f;
        float halfHeight = textSize.Y / 2f;
        
        // Create a Label for the text
        var label = new Label();
        label.Text = obj.Text;
        label.AddThemeColorOverride("font_color", obj.TextColor);
        label.AddThemeFontOverride("font", obj.Font);
        label.AddThemeFontSizeOverride("font_size", fontSize);

        // Position at center
        label.Position = new Vector2(obj.CenterX - halfWidth, obj.CenterY - halfHeight);
        label.PivotOffset = new Vector2(halfWidth, halfHeight);
        label.RotationDegrees = obj.RotationDegrees;

        _viewport.AddChild(label);

    }

    private void RenderTriangleText(TextureObject obj)
    {
        Vector2 textSize = obj.Font.GetStringSize(obj.Text, fontSize: 12);
        if (textSize.Y == 0) return;
        
        var ratio = textSize.X / textSize.Y;
        
        var bounds = ScaleRectangleInTriangle(obj.Width, ratio);

        var hh = bounds.Y / 2;
        var rotRad = Mathf.DegToRad(obj.RotationDegrees);
        
        var newX = obj.CenterX + hh * Mathf.Sin(rotRad);
        var newY = obj.CenterY + hh * Mathf.Cos(rotRad);

        var o = new TextureObject
        {
            CenterX = (int)newX,
            CenterY = (int)newY,
            Font = obj.Font,
            Height = (int)bounds.Y,
            Width = (int)bounds.X,
            Multiline = obj.Multiline,
            RotationDegrees = obj.RotationDegrees,
            Text = obj.Text,
            TextColor = obj.TextColor,
            Type = TextureObjectType.RectangleText
        };
        
        RenderRectangleText(o);
    }

    private static Vector2 ScaleRectangleInTriangle(int triangleSide, float aspectRatio)
    {
        if (aspectRatio == 0)
        {
            return Vector2.Zero;
        }

        var r = Math.Sqrt(3) / 2;

        float h = (float)(triangleSide * r / (1 + (aspectRatio * r)));

        return new Vector2(aspectRatio * h, h);
    }
    
    private void AfterRender()
    {
        // Wait for rendering (in actual use, you'd need to await a frame)
        var texture = _viewport.GetTexture();
        var image = texture.GetImage();
        
        // Create ImageTexture from the rendered image
        var imageTexture = ImageTexture.CreateFromImage(image);

        _onReady?.Invoke(imageTexture);
        
        //cleanup
        foreach(var c in _viewport.GetChildren()) c.QueueFree();
    }

    private static int AutosizeFont(string caption, Font font, int height, int width,
        int minSize, int maxSize)
    {
        var size = minSize;

        float targetWidth = width * 0.8f;
        float targetHeight = height * 0.8f;

        while (true)
        {
            var fontSize = font.GetStringSize(caption, fontSize: size);

            if (fontSize.X > targetWidth || fontSize.Y > targetHeight)
            {
                return Math.Max(size, minSize);
            }

            size++;

            if (size > maxSize) return maxSize;
        }
    }

    public enum TextureObjectType
    {
        RectangleText,
        TriangleText,
        RectangleShape,
        CircleShape,
    }

    public enum TextureShape
    {
        Square = 0, 
        Circle = 1, 
        HexPoint = 2, 
        HexFlat = 3,
        RoundedRect = 4
    }

    public class TextureDefinition
    {
        public int Width { get; set; } = 256;
        public int Height { get; set; } = 256;
        public TextureShape Shape { get; set; } = TextureShape.Square;
        public Color BackgroundColor { get; set; } = Colors.White;
        public List<TextureObject> Objects { get; set; } = new List<TextureObject>();
    }

    public class TextureObject
    {
        public TextureObjectType Type { get; set; }
        public string Text { get; set; }
        public Color TextColor { get; set; } = Colors.Black;
        public Font Font { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public int RotationDegrees { get; set; } 
        public bool Multiline { get; set; }
    }
    
}