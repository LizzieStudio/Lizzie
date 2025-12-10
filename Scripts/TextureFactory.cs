using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

public partial class TextureFactory : SubViewport
{
    private Texture2D _circleShape;
    private Texture2D _rectShape;
    private Texture2D _hexPointShape;
    private Texture2D _hexFlatShape;
    private Texture2D _roundedRectShape;
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _viewport = this;
        
        _circleShape = ResourceLoader.Load("res://Textures/Shapes/circle.png") as Texture2D;
        _rectShape = ResourceLoader.Load("res://Textures/Shapes/square.png") as Texture2D;
        _hexPointShape = ResourceLoader.Load("res://Textures/Shapes/hex.png") as Texture2D;
        _hexFlatShape = ResourceLoader.Load("res://Textures/Shapes/hexflat.png") as Texture2D;
        _roundedRectShape = ResourceLoader.Load("res://Textures/Shapes/RoundedRectangle.png") as Texture2D;
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
                if (_skip * Take >= _activeQueueEntry.TextureDefinition.Objects.Count)
                {
                    AfterRender();
                }
                else
                {
                    GenerateSecondaryTexture(_activeQueueEntry.TextureDefinition);
                }
            }
        }

        if (_activeQueueEntry == null & _textureGenerationQueue.Count > 0)
        {
            _activeQueueEntry = _textureGenerationQueue.Dequeue();
            InitiateTextureGeneration(_activeQueueEntry.TextureDefinition);
        }
    }
    
    SubViewport _viewport;
    private int _viewportUpdating;
    private TextureQueueEntry _activeQueueEntry;
    private int _skip;
    private const int Take = 10;

    
    private Queue<TextureQueueEntry> _textureGenerationQueue = new();
    
    /// <summary>
    /// Generate a texture
    /// </summary>
    public void GenerateTexture(
        TextureDefinition definition,
        Action<ImageTexture> textureReadyCallback)
    {
        var tqe = new TextureQueueEntry
        {
            TextureDefinition = definition,
            TextureReadyCallback = textureReadyCallback
        };

        _textureGenerationQueue.Enqueue(tqe);
    }

    private void InitiateTextureGeneration(TextureDefinition definition)
    {
                // For drawing, we need to render to a texture using a viewport
        // Create a SubViewport for rendering
        _viewport.Size = new Vector2I(definition.Width, definition.Height);
        _viewport.RenderTargetClearMode = SubViewport.ClearMode.Always;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _viewport.TransparentBg = true;
        
        var tr = new TextureRect();

        Texture2D texture;

        switch (definition.Shape)
        {
            case TextureShape.Square:
                texture = _rectShape;
                break;
            case TextureShape.Circle:
                texture = _circleShape;
                break;
            case TextureShape.HexPoint:
                texture = _hexPointShape;
                break;
            case TextureShape.HexFlat:
                texture = _hexFlatShape;
                break;
            case TextureShape.RoundedRect:
                texture = _roundedRectShape;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        
        var image = texture.GetImage();
        tr.Size = new Vector2(_activeQueueEntry.TextureDefinition.Width, _activeQueueEntry.TextureDefinition.Height);
        tr.ClipChildren = CanvasItem.ClipChildrenMode.Only;
        tr.Texture = ImageTexture.CreateFromImage(image);
        
        var bgRect = new ColorRect();
        bgRect.Color = definition.BackgroundColor;
        bgRect.Size = new Vector2(definition.Width, definition.Height);
        tr.AddChild(bgRect);
        
        _viewport.AddChild(tr);

        
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
    
    private void GenerateSecondaryTexture(TextureDefinition definition)
    {
        //cleanup
        foreach(var c in _viewport.GetChildren()) c.QueueFree();
        
        // Create a ColorRect for the background
        var bgRect = new ColorRect();
        bgRect.Color = definition.BackgroundColor;
        bgRect.Size = new Vector2(definition.Width, definition.Height);
        //_viewport.AddChild(bgRect);
        
        var tr = new TextureRect();
        var texture = _viewport.GetTexture();
        var image = texture.GetImage();
        tr.Size = new Vector2(_activeQueueEntry.TextureDefinition.Width, _activeQueueEntry.TextureDefinition.Height);
        tr.Texture = ImageTexture.CreateFromImage(image);
        _viewport.AddChild(tr);

        foreach (var obj in definition.Objects.Skip(_skip * Take).Take(Take))
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
        var texture = _viewport.GetTexture();
        var image = texture.GetImage();
        
        // Create ImageTexture from the rendered image
        var imageTexture = ImageTexture.CreateFromImage(image);

        _activeQueueEntry.TextureReadyCallback?.Invoke(imageTexture);
        
        //cleanup
        foreach(var c in _viewport.GetChildren()) c.QueueFree();
        _activeQueueEntry = null;
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

    public class TextureQueueEntry
    {
        public TextureDefinition TextureDefinition { get; set; }
        public bool InProcess { get; set; }
        public Action<ImageTexture> TextureReadyCallback { get; set; }
    }
}