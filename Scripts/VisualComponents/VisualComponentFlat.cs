using System;
using Godot;

public abstract partial class VisualComponentFlat : VisualComponentBase
{
    public Sprite3D FaceSprite { get; protected set; }
    public Sprite3D BackSprite { get; protected set; }

    private Texture2D _faceTexture = new ImageTexture();

    public override void _Ready()
    {
        //if (_faceTexture != null) FaceSprite.Texture = _faceTexture;
        //if (_backTexture != null) BackSprite.Texture = _backTexture;
    }

    public Texture2D FaceTexture
    {
        get => _faceTexture;
        set
        {
            _faceTexture = value;
            if (GodotObject.IsInstanceValid(FaceSprite) && FaceSprite.IsNodeReady())
                FaceSprite.Texture = value;
        }
    }

    private Texture2D _backTexture;

    public Texture2D BackTexture
    {
        get => _backTexture;
        set
        {
            _backTexture = value;
            if (GodotObject.IsInstanceValid(BackSprite) && BackSprite.IsNodeReady())
                BackSprite.Texture = value;
        }
    }

    public virtual bool ShowFace { get; protected set; }

    public void ForceFace()
    {
        SetRotationDegrees(new Vector3(RotationDegrees.X, RotationDegrees.Y, 0));
    }

    public void ForceBack()
    {
        SetRotationDegrees(new Vector3(RotationDegrees.X, RotationDegrees.Y, 180));
    }

    public override GeometryInstance3D DragMesh => ShowFace ? FaceSprite : BackSprite;
}
