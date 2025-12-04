using Godot;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = Godot.Color;
using Font = Godot.Font;
using HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment;
using Image = SixLabors.ImageSharp.Image;
using PointF = SixLabors.ImageSharp.PointF;
using Vector4 = System.Numerics.Vector4;
using VerticalAlignment = SixLabors.Fonts.VerticalAlignment;

public static class TextureBuilder
{
    public static ImageTexture Build(float height, float width, TextureBuilderOptions options)
    {
        var bgc = GodotColorToImageSharp(options.BackgroundColor);
        var tc = GodotColorToImageSharp(options.TextColor);

        //scale max dim to 128 pixels
        var max = Math.Max(width, height);
        float scale = 128f / max;

        var iHeight = (int)(height * scale);
        var iWidth = (int)(width * scale);

        using SixLabors.ImageSharp.Image<Rgba32> image = new Image<Rgba32>(iWidth, iHeight);

        image.Mutate(x => x.Fill(bgc));

        if (!string.IsNullOrWhiteSpace(options.Caption))
        {
            var coll = SystemFonts.Collection;

            FontFamily family = coll.Families.FirstOrDefault(x => x.Name == "Arial");
            FontStyle fontStyle = FontStyle.Regular;

            switch (options.FontStyle)
            {
                case TextureBuilderOptions.FontStyleEnum.Regular:
                    break;
                case TextureBuilderOptions.FontStyleEnum.Bold:
                    fontStyle = FontStyle.Bold;
                    break;
                case TextureBuilderOptions.FontStyleEnum.Italic:
                    fontStyle = FontStyle.Italic;
                    break;
                case TextureBuilderOptions.FontStyleEnum.BoldItalic:
                    fontStyle = FontStyle.BoldItalic;
                    break;
                case TextureBuilderOptions.FontStyleEnum.Narrow:
                    family = coll.Families.FirstOrDefault(x => x.Name == "Arial Narrow");
                    break;
                case TextureBuilderOptions.FontStyleEnum.Black:
                    family = coll.Families.FirstOrDefault(x => x.Name == "Arial Black");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }


            var size = AutosizeFont(options.Caption, family, fontStyle,
                iHeight, iWidth, options.MinFontSize, options.MaxFontSize);
            var font = family.CreateFont(size);

            var to = new TextOptions(font);
            to.VerticalAlignment = VerticalAlignment.Center;

            var textPath = TextBuilder.GenerateGlyphs(options.Caption, to);

            var fontRect = textPath.Bounds;
            var wOffset = (iWidth - fontRect.Width) / 2;
            var hOffset = ((iHeight - fontRect.Height) / 2) - fontRect.Y;

            var offsetP = new PointF(wOffset, hOffset);

            var finalPath = textPath.Translate(offsetP);

            image.Mutate(x => x.Fill(tc, finalPath));
        }

        var maskFile = string.Empty;

        //this should really be cached somewhere?
        switch (options.Shape)
        {
            case TextureBuilderOptions.TextureShape.Square:
                break;
            case TextureBuilderOptions.TextureShape.Circle:
                maskFile = @"C:\Users\gme\source\repos\TTSS\Textures\Shapes\circle.png";
                break;
            case TextureBuilderOptions.TextureShape.HexPoint:
                maskFile = @"C:\Users\gme\source\repos\TTSS\Textures\Shapes\hex.png";
                break;
            case TextureBuilderOptions.TextureShape.HexFlat:
                maskFile = @"C:\Users\gme\source\repos\TTSS\Textures\Shapes\hexflat.png";

                break;
            case TextureBuilderOptions.TextureShape.RoundedSquare:
                maskFile = @"C:\Users\gme\source\repos\TTSS\Textures\Shapes\RoundedRectangle.png";
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }


        if (!string.IsNullOrEmpty(maskFile))
        {
            var mask = Image.Load<Rgba32>(maskFile);

            //resize the mask to match the image
            mask.Mutate(x => x.Resize(iWidth, iHeight));

            //knock out the pixels on the image
            image.Mutate(x =>
                x.DrawImage(mask, PixelColorBlendingMode.Multiply, PixelAlphaCompositionMode.DestIn, 1));
        }

        using var s = new MemoryStream();

        image.Save(s, PngFormat.Instance);

        Godot.Image gImage = new();
        gImage.LoadPngFromBuffer(s.ToArray());

        var t = new ImageTexture();
        t.SetImage(gImage);

        return t;
    }

    private static int AutosizeFont(string caption, FontFamily fontFamily, FontStyle fontStyle, int height, int width,
        int minSize, int maxSize)
    {
        var size = minSize;

        float targetWidth = width * 0.8f;

        while (true)
        {
            var font = fontFamily.CreateFont(size, fontStyle);
            var fontRect = TextMeasurer.MeasureSize(caption, new TextOptions(font));

            if (fontRect.Width > targetWidth)
            {
                return Math.Max(size, minSize);
            }

            size++;

            if (size > maxSize) return maxSize;
        }
    }

    /// <summary>
    /// Calculate the aspect ratio of a text string in a given font
    /// </summary>
    /// <param name="caption">Text to calculate</param>
    /// <param name="fontFamily"></param>
    /// <param name="fontStyle"></param>
    /// <returns></returns>
    private static double AspectRatio(string caption, FontFamily fontFamily, FontStyle fontStyle)
    {
        if (string.IsNullOrEmpty(caption)) return 0;

        var font = fontFamily.CreateFont(12, fontStyle); //just using a placeholder size
        var fontRect = TextMeasurer.MeasureSize(caption, new TextOptions(font));

        if (fontRect.Height == 0) return 0; //error case - no text to display;
        return fontRect.Width / fontRect.Height;
    }

    public static SixLabors.ImageSharp.Color GodotColorToImageSharp(Color color) =>
        new(new Vector4(color.R, color.G, color.B,
            color.A));

    private static IImageProcessingContext ConvertToAvatar(this IImageProcessingContext context, Size size,
        float cornerRadius)
    {
        return context.Resize(new ResizeOptions
        {
            Size = size,
            Mode = ResizeMode.Crop
        }).ApplyRoundedCorners(cornerRadius);
    }


    // This method can be seen as an inline implementation of an `IImageProcessor`:
    // (The combination of `IImageOperations.Apply()` + this could be replaced with an `IImageProcessor`)
    private static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext context, float cornerRadius)
    {
        Size size = context.GetCurrentSize();
        IPathCollection corners = BuildCorners(size.Width, size.Height, cornerRadius);

        context.SetGraphicsOptions(new GraphicsOptions()
        {
            Antialias = true,

            // Enforces that any part of this shape that has color is punched out of the background
            AlphaCompositionMode = PixelAlphaCompositionMode.DestOut
        });

        // Mutating in here as we already have a cloned original
        // use any color (not Transparent), so the corners will be clipped
        foreach (IPath path in corners)
        {
            context = context.Fill(GodotColorToImageSharp(Colors.Red), path);
        }

        return context;
    }

    private static PathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius)
    {
        // First create a square
        var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

        // Then cut out of the square a circle so we are left with a corner
        IPath cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

        // Corner is now a corner shape positions top left
        // let's make 3 more positioned correctly, we can do that by translating the original around the center of the image.

        float rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
        float bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

        // Move it across the width of the image - the width of the shape
        IPath cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
        IPath cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
        IPath cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

        return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
    }

    public static ImageTexture BuildD6Texture(string[] sides)
    {
        if (sides.Length != 6)
        {
            throw new Exception($"{sides.Length} sides for D6");
        }

        var data = new List<DieFaceData>();

        //int sideSize = 42;

        //below was y of 21 and 149
        data.Add(new DieFaceData(42, 127, 85, 0, TextureBuilderOptions.FaceShape.Square));
        data.Add(new DieFaceData(127, 127, 85, 0, TextureBuilderOptions.FaceShape.Square));
        data.Add(new DieFaceData(215, 127, 85, 0, TextureBuilderOptions.FaceShape.Square));
        data.Add(new DieFaceData(42, 255, 85, 0, TextureBuilderOptions.FaceShape.Square));
        data.Add(new DieFaceData(127, 255, 85, 0, TextureBuilderOptions.FaceShape.Square));
        data.Add(new DieFaceData(215, 255, 85, 0, TextureBuilderOptions.FaceShape.Square));

        return CreateDieImage(sides, data, Colors.White);
    }

    public static ImageTexture BuildD8Texture(string[] sides)
    {
        if (sides.Length != 8)
        {
            throw new Exception($"{sides.Length} sides for D8");
        }

        var coll = SystemFonts.Collection;

        FontFamily family = coll.Families.FirstOrDefault(x => x.Name == "Arial");
        var font = family.CreateFont(12, FontStyle.Regular);

        int length = 110;

        var data = new List<DieFaceData>();

        
        data.Add(new DieFaceData( 0, 55, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
        data.Add(new DieFaceData(0, 165, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
        data.Add(new DieFaceData(127, 201, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
        data.Add(new DieFaceData(127, 92, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
        data.Add(new DieFaceData(127, 55, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
        data.Add(new DieFaceData(127, 165, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
        data.Add(new DieFaceData(255, 201, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
        data.Add(new DieFaceData(255, 92, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));

        
        /*
        Godot.Image img = new();
        img.Load("res://Textures/Dice/D8texture.png");

        var texture = ImageTexture.CreateFromImage(img);

        return texture;
        */

        return CreateDieImage(sides, data, Colors.White);
    }


  
  

    /// <summary>
    /// Determines the largest rectangle of a given aspect ratio that can fit in an equilateral triangle
    /// </summary>
    /// <param name="triangleSide">Length of side</param>
    /// <param name="aspectRatio">Aspect ratio of rectangle (w/h)</param>
    /// <returns></returns>
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

    private static ImageTexture CreateDieImage(string[] sides, List<DieFaceData> faceData, Color dieColor)
    {
        if (sides.Length != faceData.Count)
        {
            throw new Exception("Sides and face data count mismatch");
        }


        using Image<Rgba32> image = new Image<Rgba32>(256, 256);

        image.Mutate(x => x.Fill(GodotColorToImageSharp(dieColor)));

        
        var coll = SystemFonts.Collection;

        FontFamily family = coll.Families.FirstOrDefault(x => x.Name == "Calibri");
        var font = family.CreateFont(12);
        var to = new TextOptions(font);
        
        to.VerticalAlignment = VerticalAlignment.Center;
        to.HorizontalAlignment = HorizontalAlignment.Center;
        
        var textColor = GodotColorToImageSharp(Colors.Black);

        for (int i = 0; i < sides.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(sides[i])) continue;

            var opts = faceData[i];

            

            
            FontRectangle bounds = TextMeasurer.MeasureSize(sides[i], to);

            IPathCollection tps = new PathCollection();

            float scale = 1;
            
            //scale the path to fit in the face bounds
            switch (opts.Shape)
            {
                case TextureBuilderOptions.FaceShape.Square:
                    if (bounds.X < bounds.Y)
                    {
                       scale = opts.SideLength / bounds.Height;
                    }
                    else
                    {
                       scale = opts.SideLength / bounds.Width;
                    }
                    break;
                
                case TextureBuilderOptions.FaceShape.EquilateralTriangle:
                    var tr = ScaleRectangleInTriangle(opts.SideLength, bounds.Width / bounds.Height);
                    scale = tr.X / bounds.Width;
                    break;

                case TextureBuilderOptions.FaceShape.D10Triangle:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            
            
            //determine the final center position for the text.
            //It is the base point translated by half the height in the direction of rotation
            //If the angle is zero we are going to shift upwards from the base point (-y)

            var rotRad = opts.Rotation * Math.PI / 180;

            var hh = scale * bounds.Height / 2;

            if (opts.Shape == TextureBuilderOptions.FaceShape.Square)
            {
                hh = opts.SideLength * 0.5f;
            }
            
            var hw = scale * bounds.Width / 2;
            
            var finalX = (float) (opts.BasePointX + hh * Math.Sin(rotRad) );
            var finalY = (float) (opts.BasePointY - hh * Math.Cos(rotRad) );

            //Now shift from the rotated center position to the final desired position
            
            AffineTransformBuilder builder = new AffineTransformBuilder()
                .AppendScale(scale * 0.8f)
                .AppendRotationDegrees(opts.Rotation)
                .AppendTranslation(new PointF(finalX, finalY));;

            // We should probably add an extension to FontRectangle to make the conversion to Rectangle easier.
            var transform = builder.BuildMatrix(Rectangle.Round(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
            var drawingOptions = new DrawingOptions
            {
                Transform = transform
            };
            
            var tp = TextBuilder.GenerateGlyphs(sides[i], to);
            
            image.Mutate(c => c.Fill(drawingOptions, textColor, tp ));
        }

        using var s = new MemoryStream();

        image.Save(s, PngFormat.Instance);

        Godot.Image gImage = new();
        gImage.LoadPngFromBuffer(s.ToArray());

        var t = new ImageTexture();
        t.SetImage(gImage);

        return t;
    }
}


/// <summary>
/// Options that can be passed to the texture builder
/// </summary>
public class TextureBuilderOptions
{
    public enum TextureShape
    {
        Square,
        Circle,
        HexPoint,
        HexFlat,
        RoundedSquare
    }

    public TextureShape Shape { get; set; } = TextureShape.Square;
    public Color BackgroundColor { get; set; } = Colors.White;

    public string Caption { get; set; }
    public Color TextColor { get; set; } = Colors.Black;

    public int MinFontSize { get; set; } = 9;
    public int MaxFontSize { get; set; } = 72;
    public FontStyleEnum FontStyle { get; set; } = FontStyleEnum.Regular;

    public bool Outline { get; set; } = false;
    public Color OutlineColor { get; set; } = Colors.White;

    public enum FontStyleEnum
    {
        Regular,
        Bold,
        Italic,
        BoldItalic,
        Narrow,
        Black
    }

    public enum FaceShape
    {
        Square,
        EquilateralTriangle,
        D10Triangle
    }


}

public class DieFaceData
{
    public DieFaceData(int basePointX, int basePointY, int sideLength, float rotation, TextureBuilderOptions.FaceShape shape)
    {
        BasePointX = basePointX;
        BasePointY = basePointY;
        Rotation = rotation;
        SideLength = sideLength;
        Shape = shape;
    }

    public int BasePointX { get; set; }
    public int BasePointY { get; set; }
    public float Rotation { get; set; }

    public int SideLength { get; set; }

    public TextureBuilderOptions.FaceShape Shape { get; set; }
}