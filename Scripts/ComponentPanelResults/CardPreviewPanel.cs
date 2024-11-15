using Godot;
using System;
using System.Collections.Generic;

public partial class CardPreviewPanel : Panel
{
	[Export] private string[] _shapes;
	private List<Texture2D> _shapeTextures;

	private TextureRect _clipRect;
	private TextureRect _textureRect;
	private TextureRect _colorBorder;
	private Label _value;

	private int _panelSize = 300;
	private int _clipRectSize = 256;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_textureRect = GetNode<TextureRect>("%CustomTexture");
		_value = GetNode<Label>("%Value");
		_clipRect = GetNode<TextureRect>("%ClipRect");
		_colorBorder = GetNode<TextureRect>("%ColorBorder");
		
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



	
		
	public void SetBackgroundColor(Color color)
	{
		
		_colorBorder.Modulate = color;
	}

	public void SetText(string text)
	{
		_value.Text = text;
	}

	public void SetTextColor(Color color)
	{
		_value.LabelSettings.FontColor = color;
	}



	public void SetShape(TokenTextureSubViewport.TokenShape shape)
	{
		_clipRect.Texture = _shapeTextures[(int)shape];
	}

	public void SetSize(float w, float h)
	{
		if (h == 0 || w == 0) return;

		var maxs = Mathf.Max(h, w);

		float ch = h * _clipRectSize / maxs;
		float cw = w * _clipRectSize / maxs;

		var size = new Vector2(cw, ch);

		var pos = new Vector2((_panelSize - cw)/2, (_panelSize - ch)/2);

		_clipRect.Size = size;
		_clipRect.Position = pos;
		
	}

	public void DisplayQuickCard(QuickCardData card, bool showFront = true)
	{
		if (showFront)
		{
			_value.Text = card.Caption;
			_colorBorder.Modulate = card.BackgroundColor;
		}
		else
		{
			_value.Text = card.CardBackValue;
			_colorBorder.Modulate = card.CardBackColor;
		}
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
				_colorBorder.Visible = true;
				_value.Visible = true;
				_textureRect.Visible = false;
				break;
			
			case ShapeViewportMode.Texture:
				_clipRect.ClipChildren = CanvasItem.ClipChildrenMode.Only;
				_clipRect.Modulate = Colors.White;
				_colorBorder.Visible = false;
				_textureRect.Visible = true;
				break;
		}
	}
	
		
		
	#endregion
}
