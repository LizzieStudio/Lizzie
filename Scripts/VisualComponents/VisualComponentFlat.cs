using Godot;
using System;

public abstract partial class VisualComponentFlat : VisualComponentBase
{
    public Sprite3D FaceSprite { get; protected set; } = new();
    public Sprite3D BackSprite { get; protected set; } = new();
    
    public virtual bool ShowFace { get; protected set; }

    public override GeometryInstance3D DragMesh => ShowFace ? FaceSprite : BackSprite;
}
