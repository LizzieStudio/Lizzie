using Godot;

/// <summary>
/// The outline of a token's faces.
/// Saves store it as a number, so the numbers must stay the same.
/// </summary>
public enum TokenShape
{
    Rectangle = 0,
    Circle = 1,
    HexPoint = 2,
    HexFlat = 3,
}

public static class TokenShapes
{
    private static readonly string[] MaskPaths =
    [
        null, // rectangles don't need a mask
        "res://Textures/Shapes/circle.png",
        "res://Textures/Shapes/hex.png",
        "res://Textures/Shapes/hexflat.png",
    ];

    /// <summary>
    /// The shape as an image, opaque inside it and transparent outside,
    /// or null for <see cref="TokenShape.Rectangle"/>, which needs no mask.
    /// </summary>
    public static Texture2D Mask(this TokenShape shape) =>
        MaskPaths[(int)shape] is { } path ? GD.Load<Texture2D>(path) : null;

    /// <summary>
    /// A copy of <paramref name="image"/> that's transparent outside the shape.
    /// </summary>
    public static Image Clip(this TokenShape shape, Image image)
    {
        if (shape.Mask() is not { } shapeMask)
            return image;

        var width = image.GetWidth();
        var height = image.GetHeight();
        var mask = Rgba8(shapeMask.GetImage());
        mask.Resize(width, height, Image.Interpolation.Bilinear);

        var pixels = Rgba8(image).GetData();
        var maskPixels = mask.GetData();
        // The alpha channel is every 4th index.
        for (var alphaIndex = 3; alphaIndex < pixels.Length; alphaIndex += 4)
            pixels[alphaIndex] = (byte)(pixels[alphaIndex] * maskPixels[alphaIndex] / 255);
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
    }

    private static Image Rgba8(Image image)
    {
        var copy = (Image)image.Duplicate();
        if (copy.IsCompressed())
            copy.Decompress();
        copy.ClearMipmaps();
        copy.Convert(Image.Format.Rgba8);
        return copy;
    }
}
