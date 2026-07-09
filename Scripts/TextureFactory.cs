using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices.ObjectiveC;
using Godot;

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
        _roundedRectShape =
            ResourceLoader.Load("res://Textures/Shapes/RoundedRectangle.png") as Texture2D;
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

        if (!_waitingForAsset && _activeQueueEntry == null && _textureGenerationQueue.Count > 0)
        {
            _activeQueueEntry = _textureGenerationQueue.Dequeue();
            PreFetchAssetsAndInitiate(_activeQueueEntry.TextureDefinition);
        }
    }

    SubViewport _viewport;
    private int _viewportUpdating;
    private TextureQueueEntry _activeQueueEntry;
    private int _skip;
    private const int Take = 10;
    private bool _waitingForAsset;

    private Queue<TextureQueueEntry> _textureGenerationQueue = new();

    /// <summary>
    /// Generate a texture
    /// </summary>
    public void GenerateTexture(
        TextureDefinition definition,
        Action<ImageTexture> textureReadyCallback
    )
    {
        ExpandMultipleShapes(definition);

        var tqe = new TextureQueueEntry
        {
            TextureDefinition = definition,
            TextureReadyCallback = textureReadyCallback,
        };

        _textureGenerationQueue.Enqueue(tqe);
    }

    private async void PreFetchAssetsAndInitiate(TextureDefinition definition)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project != null)
        {
            var pendingAssets = new System.Collections.Generic.List<Lizzie.AssetManagement.Asset>();
            foreach (var obj in definition.Objects)
            {
                if (obj.Text != null && obj.Text.StartsWith("u:"))
                {
                    string imageName = obj.Text.Substring(2);
                    var asset = project.Images.Values.FirstOrDefault(a => a.Name == imageName);
                    if (asset != null && !asset.AssetDownloaded)
                    {
                        pendingAssets.Add(asset);
                    }
                }
            }

            if (pendingAssets.Count > 0)
            {
                _waitingForAsset = true;
                var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
                foreach (var asset in pendingAssets)
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    await ProjectService.Instance.FetchImageAsync(
                        asset,
                        _ => tcs.TrySetResult(true)
                    );
                    tasks.Add(tcs.Task);
                }
                await System.Threading.Tasks.Task.WhenAll(tasks);
                _waitingForAsset = false;
            }
        }

        InitiateTextureGeneration(definition);
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
        tr.Size = new Vector2(
            _activeQueueEntry.TextureDefinition.Width,
            _activeQueueEntry.TextureDefinition.Height
        );
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
        foreach (var c in _viewport.GetChildren())
            c.QueueFree();

        // Create a ColorRect for the background
        var bgRect = new ColorRect();
        bgRect.Color = definition.BackgroundColor;
        bgRect.Size = new Vector2(definition.Width, definition.Height);
        //_viewport.AddChild(bgRect);

        var tr = new TextureRect();
        var texture = _viewport.GetTexture();
        var image = texture.GetImage();
        tr.Size = new Vector2(
            _activeQueueEntry.TextureDefinition.Width,
            _activeQueueEntry.TextureDefinition.Height
        );
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
            else if (obj.Type == TextureObjectType.RectangleFrame)
            {
                RenderFrame(obj);
            }
            else
            {
                RenderShape(obj);
            }
        }
    }

    private void RenderFrame(TextureObject obj)
    {
        var scaleWidth = obj.Width * obj.Scale;
        var scaleHeight = obj.Height * obj.Scale;
        var pW = obj.CenterX - scaleWidth / 2;
        var pH = obj.CenterY - scaleHeight / 2;

        if (obj.BackgroundColor != Colors.Transparent)
        {
            var bgRect = new ColorRect();
            bgRect.Color = obj.BackgroundColor;
            bgRect.Position = new Vector2(pW, pH);
            bgRect.Size = new Vector2(scaleWidth, scaleHeight);
            _viewport.AddChild(bgRect);
        }

        var tr = new ReferenceRect();
        tr.EditorOnly = false;
        tr.BorderWidth = obj.FontSize;
        tr.BorderColor = obj.ForegroundColor;

        tr.Size = new Vector2(scaleWidth, scaleHeight);
        tr.Position = new Vector2(pW, pH);

        tr.PivotOffset = new Vector2(scaleWidth / 2, scaleHeight / 2);
        tr.RotationDegrees = obj.RotationDegrees;

        _viewport.AddChild(tr);
    }

    private void RenderText(TextureObject obj)
    {
        if (obj.TriangleFace)
        {
            RenderTriangleText(obj);
        }
        else
        {
            RenderRectangleText(obj, obj.Multiline);
        }
    }

    public static Vector2 GetTextBounds(Font font, int fontSize, string text)
    {
        return font.GetStringSize(text, fontSize: fontSize);
    }

    private void RenderRectangleText(TextureObject obj, bool wrap = false)
    {
        // Strip tags for measurement:
        //   1. Replace icon tags [name] / [name color=...] with "M" (excluding b/i/u BBCode).
        //   2. Strip all remaining BBCode tags ([b], [/b], etc.) so only plain text remains.
        var plainText = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(
                obj.Text,
                @"\[(?!/?(?:b|i|u)\])(\w+)(?:\s+color=[^\]]+)?\]",
                "M",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ),
            @"\[[^\]]+\]",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        string measureText = plainText.Length > 0 ? plainText : obj.Text;

        int fontSize = obj.FontSize;
        if (obj.Autosize)
        {
            fontSize = AutosizeFont(measureText, obj.Font, obj.Height, obj.Width, 6, 72, wrap);
        }

        if (fontSize == 0)
            return;

        Vector2 labelSize;
        if (wrap)
        {
            // Measure the wrapped size and clamp to obj.Height.
            float wrapWidth = obj.Width * 0.9f;
            Vector2 wrappedSize = obj.Font.GetMultilineStringSize(
                measureText,
                width: (int)wrapWidth,
                fontSize: fontSize
            );

            if (wrappedSize.X == 0 || wrappedSize.Y == 0)
                return;

            // Label spans the full width so PushParagraph can align content inside it.
            labelSize = new Vector2(obj.Width, Math.Min(wrappedSize.Y, obj.Height));
        }
        else
        {
            // Single-line: shrink the label to the measured text size so that
            // MoveOriginForAlignment places it correctly for all three alignments.
            Vector2 singleSize = obj.Font.GetStringSize(measureText, fontSize: fontSize);

            if (singleSize.X == 0 || singleSize.Y == 0)
                return;

            labelSize = singleSize;
        }

        // Create a RichTextLabel sized to the measured text area.
        var label = new RichTextLabel();
        label.AutowrapMode = wrap ? TextServer.AutowrapMode.Word : TextServer.AutowrapMode.Off;
        label.BbcodeEnabled = true;
        label.FitContent = !wrap;
        label.ClipContents = wrap;
        label.ScrollActive = false;
        label.Size = labelSize;
        label.AddThemeColorOverride("default_color", obj.ForegroundColor);
        label.AddThemeFontOverride("normal_font", obj.Font);
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);

        if (wrap)
        {
            // Drive internal text alignment through the paragraph so that all
            // three HorizontalAlignment values work inside the full-width label.
            label.PushParagraph(obj.HorizontalAlignment);
        }

        // Build content segment by segment so [img] tags become real AddImage calls,
        // scaled to match the font line height.
        PopulateRichTextLabel(label, obj.Text, obj.Font, fontSize, obj.ForegroundColor);

        if (wrap)
            label.Pop();

        label.Position = MoveOriginForAlignment(obj, labelSize);
        label.PivotOffset = new Vector2(labelSize.X / 2f, labelSize.Y / 2f);
        label.RotationDegrees = obj.RotationDegrees;
        _viewport.AddChild(label);
    }

    // Matches [iconname] or [iconname color=value], excluding the BBCode tags [b], [/b], [i], [/i], [u], [/u].
    // Group 1 = icon name, Group 2 = optional color value.
    private static readonly System.Text.RegularExpressions.Regex ImgTagRegex = new(
        @"\[(?!/?(?:b|i|u)\])(\w+)(?:\s+color=([^\]]+))?\]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled
    );

    /// <summary>
    /// Populates a RichTextLabel by splitting text on [iconname] tags.
    /// Supports an optional color attribute: [iconname color=red] or [iconname color=#rrggbb].
    /// The tags [b][/b], [i][/i], [u][/u] are passed through as BBCode unchanged.
    /// Plain-text segments are appended as BBCode; image segments are resolved
    /// via IconLibrary and inserted with AddImage scaled to the font line height.
    /// The icon is modulated to match the supplied color (or <paramref name="defaultColor"/> if none).
    /// </summary>
    private void PopulateRichTextLabel(
        RichTextLabel label,
        string text,
        Font font,
        int fontSize,
        Color defaultColor
    )
    {
        // Use the font's line height as the icon size so icons match text height.
        float lineHeight = font.GetHeight(fontSize);

        int cursor = 0;
        foreach (System.Text.RegularExpressions.Match m in ImgTagRegex.Matches(text))
        {
            // Append any plain text before this tag
            if (m.Index > cursor)
                label.AppendText(text.Substring(cursor, m.Index - cursor));

            // Group 1 = icon name, Group 2 = optional color value
            string iconName = m.Groups[1].Value.Trim();
            string colorValue = m.Groups[2].Value.Trim();

            bool iconMode = false;

            //check to see if the string has a prefix of "i:" which indicates that the icon is a built in icon - needed in case there is a naming overlap.
            if (iconName.Length > 2 && iconName.Substring(0, 2) == "i:")
            {
                iconMode = true;
                iconName = iconName.Substring(2, iconName.Length - 2);
            }

            if (string.IsNullOrEmpty(colorValue) && !iconMode)
            {
                colorValue = "white"; //user defined icons with no color defined default to white so the true colors come through
            }

            Color iconColor = string.IsNullOrEmpty(colorValue)
                ? defaultColor
                : ParseImgColor(colorValue, defaultColor);

            Texture2D iconTexture = ResolveIconTexture(iconName);

            if (iconTexture != null)
            {
                var tSize = iconTexture.GetSize();
                var iconSize = new Vector2(lineHeight * tSize.X / tSize.Y, lineHeight);
                label.AddImage(iconTexture, (int)iconSize.X, (int)iconSize.Y, iconColor);
            }

            cursor = m.Index + m.Length;
        }

        // Append any remaining plain text after the last tag
        if (cursor < text.Length)
            label.AppendText(text.Substring(cursor));
    }

    /// <summary>
    /// Parses a color string from an [img color=...] attribute.
    /// Supports:
    ///   - Named Godot colors (e.g. "red", "DodgerBlue")
    ///   - 6-digit hex with optional leading # (e.g. "#ff0000" or "ff0000")
    ///   - 8-digit hex with optional leading # (e.g. "#ff0000ff")
    /// Returns <paramref name="fallback"/> when the value cannot be parsed.
    /// </summary>
    private static Color ParseImgColor(string value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        // Strip leading '#' for uniform handling
        string hex = value.StartsWith("#") ? value.Substring(1) : value;

        // Try 6-digit hex  RRGGBB
        if (hex.Length == 6 && IsHex(hex))
        {
            uint r = Convert.ToUInt32(hex.Substring(0, 2), 16);
            uint g = Convert.ToUInt32(hex.Substring(2, 2), 16);
            uint b = Convert.ToUInt32(hex.Substring(4, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f);
        }

        // Try 8-digit hex  RRGGBBAA
        if (hex.Length == 8 && IsHex(hex))
        {
            uint r = Convert.ToUInt32(hex.Substring(0, 2), 16);
            uint g = Convert.ToUInt32(hex.Substring(2, 2), 16);
            uint b = Convert.ToUInt32(hex.Substring(4, 2), 16);
            uint a = Convert.ToUInt32(hex.Substring(6, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        // Try Godot named color (Color.FromString returns black on failure, so check first)
        var namedColor = Color.FromString(value, fallback);
        return namedColor;
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s)
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
    }

    private bool IsIconUserDefined(string name)
    {
        if (
            ProjectService.Instance.CurrentProject.Images.Any(x =>
                string.Equals(x.Value.Name, name, StringComparison.CurrentCultureIgnoreCase)
            )
        )
            return true;
        return false;
    }

    private Texture2D ResolveIconTexture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return _iconLibrary.TextureFromKey(string.Empty);

        if (IsIconUserDefined(name))
        {
            var project = ProjectService.Instance.CurrentProject;
            var asset = project?.Images.Values.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.CurrentCultureIgnoreCase)
            );

            if (asset?.Image != null)
            {
                return ImageTexture.CreateFromImage(asset.Image);
            }

            return _iconLibrary.TextureFromKey(string.Empty);
        }

        var uname = name.Replace('_', ' ');
        if (IsIconUserDefined(uname))
        {
            var project = ProjectService.Instance.CurrentProject;
            var asset = project?.Images.Values.FirstOrDefault(a =>
                string.Equals(a.Name, uname, StringComparison.CurrentCultureIgnoreCase)
            );

            if (asset?.Image != null)
            {
                return ImageTexture.CreateFromImage(asset.Image);
            }

            return _iconLibrary.TextureFromKey(string.Empty);
        }

        // Try exact key first, then case-insensitive search
        if (_iconLibrary.ContainsKey(name))
            return _iconLibrary.TextureFromKey(name);

        foreach (var key in _iconLibrary.Keys)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return _iconLibrary.TextureFromKey(key);
        }

        return _iconLibrary.TextureFromKey(string.Empty); // not-found fallback
    }

    private Vector2 MoveOriginForAlignment(TextureObject obj, Vector2 size)
    {
        //NOTE: Origin also shifts from center to top left

        float finalX = obj.CenterX;
        float finalY = obj.CenterY;

        switch (obj.HorizontalAlignment)
        {
            case HorizontalAlignment.Left:
                finalX -= obj.Width / 2;
                break;
            case HorizontalAlignment.Center:
                finalX -= size.X / 2;
                break;
            case HorizontalAlignment.Right:
                finalX += obj.Width / 2 - size.X;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        switch (obj.VerticalAlignment)
        {
            case VerticalAlignment.Top:
                finalY -= obj.Height / 2;
                break;
            case VerticalAlignment.Center:
                finalY -= size.Y / 2;
                break;
            case VerticalAlignment.Bottom:
                finalY += obj.Height / 2 - size.Y;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return new Vector2(finalX, finalY);
    }

    private void ExpandMultipleShapes(TextureDefinition definition)
    {
        var l = new List<TextureObject>();

        foreach (var obj in definition.Objects)
        {
            if (obj.Quantity > 1)
            {
                l.AddRange(BuildMultiShape(obj));
            }
        }

        definition.Objects.RemoveAll(x => x.Quantity > 1);

        definition.Objects.AddRange(l);
    }

    private List<TextureObject> BuildMultiShape(TextureObject obj)
    {
        if (obj.Quantity == 1)
            return new List<TextureObject> { obj };

        return obj.TriangleFace ? BuildMultiShapeTriangle(obj) : BuildMultiShapeRectangle(obj);
    }

    private List<TextureObject> BuildMultiShapeRectangle(TextureObject obj)
    {
        var shapes = new List<TextureObject>();

        Rect2[] nr = new Rect2[obj.Quantity];
        var h2 = obj.Height / 2;
        var w2 = obj.Width / 2;
        var h3 = obj.Height / 3;
        var w3 = obj.Width / 3;
        var w4 = obj.Width / 4;
        var h4 = obj.Height / 4;
        var w6 = obj.Width / 6;
        var h6 = obj.Height / 6;
        var h34 = h2 * 1.5f;
        var w34 = w2 * 1.5f;
        var h56 = h6 * 5;
        var w56 = w6 * 5;

        switch (obj.Quantity)
        {
            case 2:
                nr[0] = new Rect2(w4, h4, w2, h2);
                nr[1] = new Rect2(w34, h34, w2, h2);
                break;

            case 3:

                if (h2 > w2)
                {
                    nr[0] = new Rect2(w4, h2, w2, h2);
                    nr[1] = new Rect2(w34, h4, w2, h2);
                    nr[2] = new Rect2(w34, h34, w2, h2);
                }
                else
                {
                    nr[0] = new Rect2(w2, h4, w2, h2);
                    nr[1] = new Rect2(w4, h34, w2, h2);
                    nr[2] = new Rect2(w34, h34, w2, h2);
                }

                break;

            case 4:
                nr[0] = new Rect2(w4, h4, w2, h2);
                nr[1] = new Rect2(w34, h4, w2, h2);
                nr[2] = new Rect2(w4, h34, w2, h2);
                nr[3] = new Rect2(w34, h34, w2, h2);
                break;

            case 5:
                nr[0] = new Rect2(w6, h6, w3, h3);
                nr[1] = new Rect2(w56, h6, w3, h3);
                nr[2] = new Rect2(w2, h2, w3, h3);
                nr[3] = new Rect2(w6, h56, w3, h3);
                nr[4] = new Rect2(w56, h56, w3, h3);
                break;

            case 6:
                if (w2 > h2)
                {
                    nr[0] = new Rect2(w6, h6, w3, h3);
                    nr[1] = new Rect2(w2, h6, w3, h3);
                    nr[2] = new Rect2(w56, h6, w3, h3);
                    nr[3] = new Rect2(w6, h56, w3, h3);
                    nr[4] = new Rect2(w2, h56, w3, h3);
                    nr[5] = new Rect2(w56, h56, w3, h3);
                }
                else
                {
                    nr[0] = new Rect2(w6, h6, w3, h3);
                    nr[1] = new Rect2(w6, h2, w3, h3);
                    nr[2] = new Rect2(w6, h56, w3, h3);
                    nr[3] = new Rect2(w56, h6, w3, h3);
                    nr[4] = new Rect2(w56, h2, w3, h3);
                    nr[5] = new Rect2(w56, h56, w3, h3);
                }

                break;

            case 7:
                if (w2 > h2)
                {
                    nr[0] = new Rect2(w6, h6, w3, h3);
                    nr[1] = new Rect2(w2, h6, w3, h3);
                    nr[2] = new Rect2(w56, h6, w3, h3);
                    nr[3] = new Rect2(w6, h56, w3, h3);
                    nr[4] = new Rect2(w2, h56, w3, h3);
                    nr[5] = new Rect2(w56, h56, w3, h3);
                    nr[6] = new Rect2(w2, h2, w3, h3);
                }
                else
                {
                    nr[0] = new Rect2(w6, h6, w3, h3);
                    nr[1] = new Rect2(w6, h2, w3, h3);
                    nr[2] = new Rect2(w6, h56, w3, h3);
                    nr[3] = new Rect2(w56, h6, w3, h3);
                    nr[4] = new Rect2(w56, h2, w3, h3);
                    nr[5] = new Rect2(w56, h56, w3, h3);
                    nr[6] = new Rect2(w2, h2, w3, h3);
                }

                break;

            case 8:
                nr[0] = new Rect2(w6, h6, w3, h3);
                nr[1] = new Rect2(w2, h6, w3, h3);
                nr[2] = new Rect2(w56, h6, w3, h3);
                nr[3] = new Rect2(w6, h56, w3, h3);
                nr[4] = new Rect2(w2, h56, w3, h3);
                nr[5] = new Rect2(w56, h56, w3, h3);
                nr[6] = new Rect2(w6, h2, w3, h3);
                nr[7] = new Rect2(w56, h2, w3, h3);
                break;

            case 9:
                nr[0] = new Rect2(w6, h6, w3, h3);
                nr[1] = new Rect2(w2, h6, w3, h3);
                nr[2] = new Rect2(w56, h6, w3, h3);
                nr[3] = new Rect2(w6, h56, w3, h3);
                nr[4] = new Rect2(w2, h56, w3, h3);
                nr[5] = new Rect2(w56, h56, w3, h3);
                nr[6] = new Rect2(w6, h2, w3, h3);
                nr[7] = new Rect2(w2, h2, w3, h3);
                nr[8] = new Rect2(w56, h2, w3, h3);
                break;

            default:
                nr[0] = new Rect2(w2 / 2, h2 / 2, w2, h2);
                nr[1] = new Rect2(w2 * 1.5f, h2 / 2, w2, h2);
                nr[2] = new Rect2(w2 / 2, h2 * 1.5f, w2, h2);
                nr[3] = new Rect2(w2 * 1.5f, h2 * 1.5f, w2, h2);
                break;
        }

        foreach (var r in nr)
        {
            var to = new TextureObject(obj);
            to.CenterX = (int)(r.Position.X + obj.CenterX - w2);
            to.CenterY = (int)(r.Position.Y + obj.CenterY - h2);
            to.Height = (int)r.Size.Y;
            to.Width = (int)r.Size.X;
            to.Scale = 0.9f;
            to.Quantity = 1;
            to.RotationDegrees = obj.RotationDegrees;
            shapes.Add(to);
        }

        return shapes;
    }

    private List<TextureObject> BuildMultiShapeTriangle(TextureObject obj)
    {
        var shapes = new List<TextureObject>();

        var tris = new Vector3[9]; //x, y = origin. z = rotation degrees
        var nt = new Vector3[obj.Quantity];

        var x = obj.CenterX;
        var y = obj.CenterY;
        var rr = Mathf.DegToRad(obj.RotationDegrees);
        int h2;
        int b6;

        if (obj.Quantity < 5)
        {
            //triangles for 2-4
            int b4 = obj.Width / 4;
            h2 = (int)(Math.Sqrt(3) * obj.Width / 4);

            tris[0] = new Vector3(0, h2, 0);
            tris[1] = new Vector3(-b4, 0, 0);
            tris[2] = new Vector3(0, h2 * 3 / 4, 180);
            tris[3] = new Vector3(b4, 0, 0);
        }
        else
        {
            //triangles for 5-9
            float s1 = 0.8f;
            float x1 = 1.1f;
            float s2 = 0.7f;
            float x2 = 0.1f;
            h2 = (int)(Math.Sqrt(3) * obj.Width / 6);
            b6 = obj.Width / 6;
            tris[0] = new Vector3(0, 2 * h2, 0);
            tris[1] = new Vector3(-b6, h2 * x1, 0);
            tris[2] = new Vector3(0, h2 * 2 * s1, 180);
            tris[3] = new Vector3(b6, h2 * x1, 0);
            tris[4] = new Vector3(-2 * b6, h2 * x2, 0);
            tris[5] = new Vector3(-b6, h2 * s2, 180);
            tris[6] = new Vector3(0, h2 * x2, 0);
            tris[7] = new Vector3(b6, h2 * s2, 180);
            tris[8] = new Vector3(2 * b6, h2 * x2, 0);
        }

        switch (obj.Quantity)
        {
            case 2:
                nt[0] = tris[0];
                nt[1] = tris[2];
                break;

            case 3:
                nt[0] = tris[0];
                nt[1] = tris[1];
                nt[2] = tris[3];
                break;

            case 4:
                nt[0] = tris[0];
                nt[1] = tris[1];
                nt[2] = tris[2];
                nt[3] = tris[3];
                break;

            case 5:
                nt[0] = tris[0];
                nt[1] = tris[1];
                nt[2] = tris[3];
                nt[3] = tris[5];
                nt[4] = tris[7];
                break;

            case 6:
                nt[0] = tris[0];
                nt[1] = tris[1];
                nt[2] = tris[3];
                nt[3] = tris[4];
                nt[4] = tris[6];
                nt[5] = tris[8];
                break;

            case 7:
                nt[0] = tris[0];
                nt[1] = tris[1];
                nt[2] = tris[2];
                nt[3] = tris[3];
                nt[4] = tris[4];
                nt[5] = tris[6];
                nt[6] = tris[8];
                break;

            case 8:
                nt[0] = tris[0];
                nt[1] = tris[1];
                nt[2] = tris[2];
                nt[3] = tris[3];
                nt[4] = tris[4];
                nt[5] = tris[5];
                nt[6] = tris[7];
                nt[7] = tris[8];
                break;

            case 9:
                nt[0] = tris[0];
                nt[1] = tris[1];
                nt[2] = tris[2];
                nt[3] = tris[3];
                nt[4] = tris[4];
                nt[5] = tris[5];
                nt[6] = tris[6];
                nt[7] = tris[7];
                nt[8] = tris[8];
                break;
        }

        foreach (var t in nt)
        {
            var to = new TextureObject(obj);

            to.CenterX = (int)(obj.CenterX + t.X * Mathf.Cos(rr) + t.Y * Mathf.Sin(rr));
            to.CenterY = (int)(obj.CenterY + t.Y * Mathf.Cos(rr) + t.X * Mathf.Sin(rr));

            to.Height = h2;
            to.Width = h2;
            to.Quantity = 1;
            to.RotationDegrees = obj.RotationDegrees + (int)t.Z;
            shapes.Add(to);
        }

        return shapes;
    }

    private void RenderTriangleText(TextureObject obj)
    {
        Vector2 textSize = obj.Font.GetStringSize(obj.Text, fontSize: 12);
        if (textSize.Y == 0)
            return;

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
            Type = TextureObjectType.Text,
        };

        RenderRectangleText(o, o.Multiline);
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
        var ratio = 1; //shapes are always bounded by a square

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
            Type = obj.Type,
            Text = obj.Text,
        };

        RenderShapeInRectangle(o);
    }

    private void RenderShapeInRectangle(TextureObject obj)
    {
        var tr = new TextureRect();
        Texture2D texture;

        bool externalMode = false;

        // "u:<name>" means fetch from the current project's image library
        if (obj.Text != null && obj.Text.StartsWith("u:"))
        {
            string imageName = obj.Text.Substring(2);
            var project = ProjectService.Instance.CurrentProject;
            var asset = project?.Images.Values.FirstOrDefault(a => a.Name == imageName);
            if (asset?.Image != null)
            {
                texture = ImageTexture.CreateFromImage(asset.Image);
            }
            else
            {
                texture = _iconLibrary.TextureFromKey(string.Empty);
            }

            externalMode = true;
        }
        else
        {
            //caption has the key to the icon
            texture = _iconLibrary.TextureFromKey(obj.Text);
        }

        var scaleWidth = obj.Width * obj.Scale;
        var scaleHeight = obj.Height * obj.Scale;
        var halfWidth = scaleWidth / 2;
        var halfHeight = scaleHeight / 2;

        var image = texture.GetImage();

        if (obj.Stretch)
        {
            tr.Size = new Vector2(scaleWidth, scaleHeight);
            tr.StretchMode = TextureRect.StretchModeEnum.Scale;
            tr.Position = new Vector2(obj.CenterX - scaleWidth / 2, obj.CenterY - scaleHeight / 2);
        }
        else
        {
            //resize texture to fit in box for alignment
            Vector2 imgSize = image.GetSize();
            if (imgSize.X > imgSize.Y)
            {
                imgSize *= (scaleWidth / imgSize.X);
                if (imgSize.Y > scaleHeight)
                {
                    imgSize *= (scaleHeight / imgSize.Y);
                }
            }
            else
            {
                imgSize *= (scaleHeight / imgSize.Y);
                if (imgSize.X > scaleWidth)
                {
                    imgSize *= (scaleWidth / imgSize.X);
                }
            }

            tr.Size = imgSize;
            tr.CustomMinimumSize = imgSize;
            tr.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            tr.Position = MoveOriginForAlignment(obj, imgSize);
        }

        //image.Resize((int)scaleWidth, (int)scaleHeight);

        tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;

        tr.Texture = ImageTexture.CreateFromImage(image);

        if (!externalMode)
        {
            tr.ClipChildren = CanvasItem.ClipChildrenMode.Only;
            var bgRect = new ColorRect();
            bgRect.Color = obj.ForegroundColor;
            bgRect.Size = new Vector2(scaleWidth, scaleHeight);
            tr.AddChild(bgRect);
        }

        tr.PivotOffset = new Vector2(tr.Size.X / 2f, tr.Size.Y / 2f);
        //tr.PivotOffset = new Vector2(halfWidth, halfHeight);
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
        foreach (var c in _viewport.GetChildren())
            c.QueueFree();
        _activeQueueEntry = null;
    }

    private static int AutosizeFont(
        string caption,
        Font font,
        int height,
        int width,
        int minSize,
        int maxSize,
        bool wrap = false
    )
    {
        float targetHeight = height * 0.9f;
        float targetWidth = width * 0.9f;

        int last = minSize;
        for (int size = minSize; size <= maxSize; size++)
        {
            Vector2 measured = wrap
                ? font.GetMultilineStringSize(caption, width: (int)targetWidth, fontSize: size)
                : font.GetStringSize(caption, fontSize: size);

            bool tooBig = wrap
                ? measured.Y > targetHeight
                : measured.X > targetWidth || measured.Y > targetHeight;

            if (tooBig)
                return last;

            last = size;
        }
        return maxSize;
    }

    public enum TextureObjectType
    {
        Text,
        CoreShape,
        ExtendedShape,
        UserShape,
        RectangleFrame,
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
        RoundedRect = 4,
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
        public enum AnchorPoint
        {
            TopLeft,
            TopCenter,
            TopRight,
            MiddleLeft,
            MiddleCenter,
            MiddleRight,
            BottomLeft,
            BottomCenter,
            BottomRight,
        };

        public TextureObject()
        {
            CenterX = Width / 2;
            CenterY = Height / 2;
            Anchor = AnchorPoint.MiddleCenter;
        }

        public TextureObject(TextureObject obj)
        {
            Type = obj.Type;
            TriangleFace = obj.TriangleFace;
            Text = obj.Text;
            ForegroundColor = obj.ForegroundColor;
            Font = obj.Font;
            Height = obj.Height;
            Width = obj.Width;
            CenterX = obj.CenterX;
            CenterY = obj.CenterY;
            Anchor = obj.Anchor;
            RotationDegrees = obj.RotationDegrees;
            Quantity = obj.Quantity;
            Multiline = obj.Multiline;
            Stretch = obj.Stretch;
            BackgroundColor = obj.BackgroundColor;
        }

        public TextureObjectType Type { get; set; }

        public bool TriangleFace { get; set; }
        public string Text { get; set; }
        public Color ForegroundColor { get; set; } = Colors.Black;
        public Color BackgroundColor { get; set; } = Colors.Transparent;
        public Font Font { get; set; }

        public int FontSize { get; set; } = 12;

        public bool Autosize { get; set; } = true;
        public int Height { get; set; }
        public int Width { get; set; }
        public int CenterX { get; set; }
        public int CenterY { get; set; }

        public float Scale { get; set; } = 1f;

        public AnchorPoint Anchor { get; set; }
        public int RotationDegrees { get; set; }
        public bool Multiline { get; set; }

        public int Quantity { get; set; }

        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

        public bool Stretch { get; set; } = false;

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

        public static TrackElement.TrackTypeEnum TrackStringToEnum(string trackType)
        {
            //just look at the first letter of the keyword to give the user a break if they are using their
            //own spreadsheet values
            return trackType.ToUpper()[0] switch
            {
                'H' => TrackElement.TrackTypeEnum.Horizontal,
                'V' => TrackElement.TrackTypeEnum.Vertical,
                'P' => TrackElement.TrackTypeEnum.Perimeter,
                'G' => TrackElement.TrackTypeEnum.Grid,
                _ => TrackElement.TrackTypeEnum.Horizontal,
            };
        }
    }

    public class TextureQueueEntry
    {
        public TextureDefinition TextureDefinition { get; set; }
        public bool InProcess { get; set; }
        public Action<ImageTexture> TextureReadyCallback { get; set; }
    }
}
