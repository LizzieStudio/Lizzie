using Godot;
using System;

public class TokenTextureParameters 
{
    public float Height { get; set; }
    public float Width { get; set; }
    public int TextureMaxSize { get; set; } = 128;
    public Color BackgroundColor { get; set; } = Colors.White;
    public string Caption { get; set; }
    public int FontSize { get; set; } = 24;
    public Color CaptionColor { get; set; }
    public TokenTextureSubViewport.TokenShape Shape { get; set; }
}
