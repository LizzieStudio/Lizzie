using System;
using Godot;

public abstract partial class VisualComponentFlat : VisualComponentBase
{
    public Sprite3D FaceSprite { get; protected set; } = new();
    public Sprite3D BackSprite { get; protected set; } = new();

    private Texture2D _faceTexture;

    public Texture2D FaceTexture
    {
        get => _faceTexture;
        set
        {
            _faceTexture = value;
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
            BackSprite.Texture = value;
        }
    }

    public virtual bool ShowFace { get; protected set; }

    public void ForceFace()
    {
        RotationDegrees = new Vector3(RotationDegrees.X, RotationDegrees.Y, 0);
    }

    public void ForceBack()
    {
        RotationDegrees = new Vector3(RotationDegrees.X, RotationDegrees.Y, 180);
    }

    public override GeometryInstance3D DragMesh => ShowFace ? FaceSprite : BackSprite;
}
