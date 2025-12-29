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
    private Texture2D _triangleShape;
    private Texture2D _starShape;
    private Texture2D _pentagonShape;
    private IconLibrary _iconLibrary = new();
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _viewport = this;
        
        _circleShape = ResourceLoader.Load("res://Textures/Shapes/circle.png") as Texture2D;
        _rectShape = ResourceLoader.Load("res://Textures/Shapes/square.png") as Texture2D;
        _hexPointShape = ResourceLoader.Load("res://Textures/Shapes/hex.png") as Texture2D;
        _hexFlatShape = ResourceLoader.Load("res://Textures/Shapes/hexflat.png") as Texture2D;
        _roundedRectShape = ResourceLoader.Load("res://Textures/Shapes/RoundedRectangle.png") as Texture2D;
        _triangleShape = ResourceLoader.Load("res://Textures/Shapes/triangle.png") as Texture2D;
        _starShape = ResourceLoader.Load("res://Textures/Shapes/star.png") as Texture2D;
        _pentagonShape = ResourceLoader.Load("res://Textures/Shapes/pentagon.png") as Texture2D;
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
            case TokenShape.Square:
                texture = _rectShape;
                break;
            case TokenShape.Circle:
                texture = _circleShape;
                break;
            case TokenShape.HexPoint:
                texture = _hexPointShape;
                break;
            case TokenShape.HexFlat:
                texture = _hexFlatShape;
                break;
            case TokenShape.RoundedRect:
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

        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        
        _viewportUpdating = 2;
        _skip = 0;

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

        RenderObjects(definition);
        
        // Get the rendered texture
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        
        _viewportUpdating = 2;
        _skip++;
    }

    private void RenderObjects(TextureDefinition definition)
    {
        foreach (var obj in definition.Objects.Skip(_skip * Take).Take(Take))
        {
            if (obj.Type == TextureObjectType.Text)
            {
                RenderText(obj);
            }
            else
            {
                RenderShape(obj);
            }

        }
    }
    
    private void RenderText(TextureObject obj)
    {
        if (obj.TriangleFace)
        {
            RenderTriangleText(obj);
        }
        else
        {
            RenderRectangleText(obj);
        }
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
        label.AddThemeColorOverride("font_color", obj.ForegroundColor);
        label.AddThemeFontOverride("font", obj.Font);
        label.AddThemeFontSizeOverride("font_size", fontSize);

        float xOffset = 0;
        float yOffset = 0;
        // Position at based on anchor
        switch (obj.Anchor)
        {
            case TextureObject.AnchorPoint.TopLeft:
                break;
            case TextureObject.AnchorPoint.TopCenter:
                xOffset = halfWidth;
                break;
            case TextureObject.AnchorPoint.TopRight:
                xOffset = textSize.X;
                break;
            case TextureObject.AnchorPoint.MiddleLeft:
                yOffset = halfHeight;
                break;
            case TextureObject.AnchorPoint.MiddleCenter:
                xOffset = halfWidth;
                yOffset = halfHeight;
                break;
            case TextureObject.AnchorPoint.MiddleRight:
                xOffset = textSize.X;
                yOffset = halfHeight;
                break;
            case TextureObject.AnchorPoint.BottomLeft:
                yOffset = textSize.Y;
                break;
            case TextureObject.AnchorPoint.BottomCenter:
                xOffset = halfWidth;
                yOffset = textSize.Y;
                break;
            case TextureObject.AnchorPoint.BottomRight:
                xOffset = textSize.X;
                yOffset = textSize.Y;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        
        label.Position = new Vector2(obj.CenterX - xOffset, obj.CenterY - yOffset);
        
        label.PivotOffset = new Vector2(halfWidth, halfHeight); //always pivot at the center
        
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
            ForegroundColor = obj.ForegroundColor,
            TriangleFace = false,
            Type = TextureObjectType.Text
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

    private void RenderShape(TextureObject obj)
    {
        if (obj.TriangleFace)
        {
            RenderShapeInTriangle(obj);
        }
        else
        {
            RenderShapeInRectangle(obj);
        }
    }

    private void RenderShapeInTriangle(TextureObject obj)
    {
        var ratio = 1;  //shapes are always bounded by a square
        
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
            ForegroundColor = obj.ForegroundColor,
            TriangleFace = false,
            Type = obj.Type
        };
        
        RenderShapeInRectangle(o);
    }
    
    private void RenderShapeInRectangle(TextureObject obj)
    {
        var tr = new TextureRect();
        Texture2D texture;
        
        //caption has the key to the icon
        texture = _iconLibrary.TextureFromKey(obj.Text);

        float scale = 0.8f;
        
        var scaleWidth = obj.Width * scale;
        var scaleHeight = obj.Height * scale;
        
        var image = texture.GetImage();
        //image.Resize((int)scaleWidth, (int)scaleHeight);
        
        tr.Size = new Vector2(scaleWidth, scaleHeight);
        tr.CustomMinimumSize = new Vector2(scaleWidth, scaleHeight);
        tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        tr.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
        tr.ClipChildren = CanvasItem.ClipChildrenMode.Only;
        tr.Texture = ImageTexture.CreateFromImage(image);

        
        
        var bgRect = new ColorRect();
        bgRect.Color = obj.ForegroundColor;
        bgRect.Size = new Vector2(scaleWidth, scaleHeight) ;
        tr.AddChild(bgRect);
        
        var halfWidth = scaleWidth / 2;
        var halfHeight = scaleHeight / 2;
        tr.Position = new Vector2(obj.CenterX - halfWidth, obj.CenterY - halfHeight);
        tr.PivotOffset = new Vector2(halfWidth, halfHeight);
        tr.RotationDegrees = obj.RotationDegrees;

        _viewport.AddChild(tr);
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
        Text,
        CoreShape,
        ExtendedShape,
        UserShape,
        /*
        RectangleShape,
        CircleShape,
        HexFlatUpShape,
        HexPointUpShape,
        TriangleShape,
        StarShape,
        PentagonShape
        */
    }

    public enum TokenShape
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
        public TokenShape Shape { get; set; } = TokenShape.Square;
        public Color BackgroundColor { get; set; } = Colors.White;
        public List<TextureObject> Objects { get; set; } = new List<TextureObject>();
    }

    public class TextureObject
    {
        public enum AnchorPoint {TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight};
        
        public TextureObject()
        {
            CenterX = Width / 2;
            CenterY = Height / 2;
            Anchor = AnchorPoint.MiddleCenter;
        }
        
        public TextureObjectType Type { get; set; }
        
        public bool TriangleFace { get; set; }
        public string Text { get; set; }
        public Color ForegroundColor { get; set; } = Colors.Black;
        public Font Font { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        
        public AnchorPoint Anchor { get; set; }
        public int RotationDegrees { get; set; } 
        public bool Multiline { get; set; }


        public static AnchorPoint AnchorStringToEnum(string anchor)
        {
            switch (anchor.ToLower())
            {
                case "tl":
                    return AnchorPoint.TopLeft;
                case "tc":
                    return AnchorPoint.TopCenter;
                case "tr":
                    return AnchorPoint.TopRight;
                case "ml":
                    return AnchorPoint.MiddleLeft;
                case "mc":
                    return AnchorPoint.MiddleCenter;
                case "mr":
                    return AnchorPoint.MiddleRight;
                case "bl":
                    return AnchorPoint.BottomLeft;
                case "bc":
                    return AnchorPoint.BottomCenter;
                case "br":
                    return AnchorPoint.BottomRight;
                
                default:
                    return AnchorPoint.TopLeft;
            }
        } 
        
        public static string AnchorEnumToString(AnchorPoint anchorEnum)
        {
            switch (anchorEnum)
            {
                case AnchorPoint.TopLeft:
                    return "TL";
                    
                case AnchorPoint.TopCenter:
                    return "TC";
                    
                case AnchorPoint.TopRight:
                    return "TR";
                    
                case AnchorPoint.MiddleLeft:
                    return "ML";
                    
                case AnchorPoint.MiddleCenter:
                    return "MC";
                    
                case AnchorPoint.MiddleRight:
                    return "MR";
                    
                case AnchorPoint.BottomLeft:
                    return "BL";
                    
                case AnchorPoint.BottomCenter:
                    return "BC";
                    
                case AnchorPoint.BottomRight:
                    return "BR";
                    
                default:
                    throw new ArgumentOutOfRangeException(nameof(anchorEnum), anchorEnum, null);
            }
        }
    }
    
    

    public class TextureQueueEntry
    {
        public TextureDefinition TextureDefinition { get; set; }
        public bool InProcess { get; set; }
        public Action<ImageTexture> TextureReadyCallback { get; set; }
    }
}