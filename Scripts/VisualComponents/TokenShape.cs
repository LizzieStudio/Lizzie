using Godot;

namespace Lizzie;

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
}
