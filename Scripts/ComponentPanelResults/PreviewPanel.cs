using Godot;
using System;
using System.Collections.Generic;

public partial class PreviewPanel : Panel
{
	[Export] private string[] _shapes;
	private List<Texture2D> _shapeTextures;

	private TextureRect _clipRect;
	private TextureRect _textureRect;
	private Label _label;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_textureRect = GetNode<TextureRect>("ClipRect/TextureRect");
		_label = GetNode<Label>("ClipRect/Label");
		_clipRect = GetNode<TextureRect>("ClipRect");
		LoadShapeTextures();
	}

	private void LoadShapeTextures()
	{
		_shapeTextures = new List<Texture2D>();
		foreach (var s in _shapes)
		{
			_shapeTextures.Add(LoadTexture(s));	
		}
	}
	
	private ImageTexture LoadTexture(string filename)
	{
		var image = new Image();
		var err = image.Load(filename);

		if (err == Error.Ok)
		{
			var texture = new ImageTexture();
			texture.SetImage(image);
			return texture;
		}

		return new ImageTexture();
	}

	#region Viewports



	private Color _bgColor = Colors.White;
		
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



	public void SetShape(TokenTextureSubViewport.TokenShape shape)
	{
		_clipRect.Texture = _shapeTextures[(int)shape];
	}

	public void SetSize(float w, float h)
	{
		if (h == 0 || w == 0) return;
				
		var maxs = Math.Max(w, h);

		Scale = new Vector2(w / maxs, h / maxs);
		_label.Scale = new Vector2(maxs / w, maxs / h);		//reverse scale so text remains normal aspect
				
		var _previewBasePos = new Vector2(86, 11);
		var _previewBaseSize = 128;
				
		//move the offset position to recenter the shape
		float nx = _previewBasePos.X + (_previewBaseSize * (1 - Scale.X) / 2);
		float ny = _previewBasePos.Y + (_previewBaseSize * (1 - Scale.Y) / 2);

		_clipRect.Position = new Vector2(nx, ny);
				
		float lx = _previewBaseSize * (1 - _label.Scale.X) / 2;		//need to use the inverse scale since
		float ly = _previewBaseSize * (1 - _label.Scale.Y) / 2;		//to match the label scale is
		_label.Position = new Vector2(lx, ly);
	}
	
	public void SetTexture(ImageTexture texture)
	{
		int h = (int)Math.Floor(_textureRect.Size.Y);
		int w = (int)Math.Floor(_textureRect.Size.X);
		
		texture.SetSizeOverride(new Vector2I(w,h));
		_textureRect.Texture = texture;
	}

	public enum ShapeViewportMode {Shape, Texture}

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
	
		
		
	#endregion
}
