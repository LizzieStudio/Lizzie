using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;
using static VcToken;

public partial class VcDie : VisualComponentBase
{
    [Export]
    private int _sides;

    [Export]
    private Vector3[] _sideRotations;

    private MeshInstance3D _mainMesh;

    private TextureFactory _textureFactory;
    private QuickTextureField[] _sideData;
    private Color _dieColor;

    public override void _Ready()
    {
        base._Ready();

        _mainMesh = GetNode<MeshInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        ComponentType = VisualComponentType.Die;
    }

    private bool _rollInProcess;
    private Vector3 _rollTargetRotation;
    private double _rollDuration = 0.5;
    private double _rollTime;

    public override void _Process(double delta)
    {
        if (_rollInProcess)
        {
            _rollTime += delta;
            if (_rollTime > _rollDuration)
            {
                Rotation = _rollTargetRotation;
                _rollInProcess = false;
            }
            else
            {
                int side = (int)(GD.Randi() % _sides + 1);
                if (side <= _sideRotations.Length)
                    Rotation = _sideRotations[side - 1] * (3.14159f / 180f); //convert to radians
            }
        }
    }

    public override float MaxAxisSize => Scale.X;
    public override GeometryInstance3D DragMesh => _mainMesh;

    public override Effect[] ProcessCommand(VisualCommand command)
    {
        // As long as the commands stay in order, this will work.
        if ((int)command >= (int)VisualCommand.Num1 && (int)command <= (int)VisualCommand.Num20)
        {
            int side = (int)command + 1 - (int)VisualCommand.Num1;
            var t = ShowSide(side);
            return t != null ? [t] : base.ProcessCommand(command);
        }

        if (command == VisualCommand.Roll)
            return [BuildRoll()];

        return base.ProcessCommand(command);
    }

    public override List<MenuCommand> GetMenuCommands()
    {
        var l = new List<MenuCommand>();

        foreach (var i in base.GetMenuCommands())
        {
            l.Add(i);
        }

        l.Add(new MenuCommand(VisualCommand.Roll));

        return l;
    }

    private TransformEffect BuildRoll()
    {
        // The rolling client picks the target face
        var side = (int)(GD.Randi() % _sides + 1);

        var t = TransformEffect.Capture(this);
        if (side <= _sideRotations.Length)
            t.Rotation = _sideRotations[side - 1] * (3.14159f / 180f); // degrees to radians

        return t;
    }

    public void AnimateRoll(Vector3 targetRotation)
    {
        _rollTargetRotation = targetRotation;
        _rollInProcess = true;
        _rollTime = 0;
    }

    private TransformEffect ShowSide(int side)
    {
        if (side > _sideRotations.Length)
            return null;

        var t = TransformEffect.Capture(this);
        t.Rotation = _sideRotations[side - 1] * (3.14159f / 180f); //convert to radians
        return t;
    }

    private TokenBuildMode _mode;

    public override bool Setup(
        ComponentParameters parameters,
        string datasetRow,
        TextureFactory textureFactory
    )
    {
        DataSetRow = datasetRow;

        base.Setup(parameters, datasetRow, textureFactory);
        var p = (DieParameters)parameters;

        _textureFactory = textureFactory;

        _mainMesh = GetNode<MeshInstance3D>("ObjectMesh");

        _sideData = p.Sides;

        _frontTemplateRef = p.FrontTemplate;
        _datasetRef = p.Dataset;

        _mode = p.Mode;

        _sides = p.SideCount;

        var dieColor = Colors.White;
        _dieColor = p.Color;

        if (p.Size <= 0)
            return false;

        float size = p.Size / 10f;

        YHeight = size;

        Scale = new Vector3(size, size, size);

        switch (_mode)
        {
            case TokenBuildMode.Quick:
                BuildQuick();
                break;
            case TokenBuildMode.Custom:
                break;
            case TokenBuildMode.Template:
                BuildTemplate();
                break;
            default:
                return false;
        }

        if (_mainMesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D material)
        {
            material.AlbedoColor = dieColor;
        }

        return true;
    }

    private void BuildQuick()
    {
        if (_sideData != null && _sideData.Length > 0)
        {
            if (_sideData.Length == 6)
            {
                var tx = D6TextureDefinition(_sideData, _dieColor);

                _textureFactory.GenerateTexture(tx, TextureDone);
                return;
            }

            if (_sideData.Length == 8)
            {
                var tx = D8TextureDefinition(_sideData, _dieColor);
                _textureFactory.GenerateTexture(tx, TextureDone);
                return;
            }

            if (_sideData.Length == 10)
            {
                var tx = D10TextureDefinition(_sideData, _dieColor);
                _textureFactory.GenerateTexture(tx, TextureDone);
                return;
            }

            if (_sideData.Length == 12)
            {
                var tx = D12TextureDefinition(_sideData, _dieColor);
                _textureFactory.GenerateTexture(tx, TextureDone);
                return;
            }

            if (_sideData.Length == 20)
            {
                var tx = D20TextureDefinition(_sideData, _dieColor);
                _textureFactory.GenerateTexture(tx, TextureDone);
                return;
            }
        }
    }

    private void BuildTemplate()
    {
        var tc = new TextureContext
        {
            CurrentRowName = DataSetRow,
            Dpi = 100,
            ParentSize = new Vector2(512, 512),
        };

        if (_sides == 6)
        {
            tc.ParentSize = new Vector2(512, 340);
        }

        var ds = ProjectService.Instance.GetDataSet(_datasetRef);
        tc.DataSet = ds;

        var template = ProjectService.Instance.GetTemplate(_frontTemplateRef);
        if (template == null)
            return;

        var tx = TemplateEngine.GenerateTextureDefinition(template, tc);
        _textureFactory.GenerateTexture(tx, TextureDone);
    }

    private SnowportId _frontTemplateRef;
    private SnowportId _datasetRef;

    private void TextureDone(ImageTexture texture)
    {
        if (!IsInstanceValid(_mainMesh) || _mainMesh == null)
            return;
        var mat = new StandardMaterial3D();
        mat.AlbedoTexture = texture;
        _mainMesh.MaterialOverride = mat;

        var d = texture.GetImage();
        //d.SavePng(@"c:\winwam5\d8.png");
    }

    private TextureFactory.TextureDefinition D6TextureDefinition(
        QuickTextureField[] sides,
        Color color
    )
    {
        var font = new SystemFont();

        var tx = new TextureFactory.TextureDefinition
        {
            BackgroundColor = color,
            Height = 170,
            Width = 256,
            Shape = TextureFactory.TokenShape.Square,
        };

        var to = new TextureFactory.TextureObject
        {
            Scale = 0.8f,
            CenterX = 42,
            CenterY = 42,
            Font = font,
            Height = 85,
            Width = 85,
            Type = sides[0].FaceType,
            RotationDegrees = 0,
            Text = sides[0].Caption,
            TriangleFace = false,
            ForegroundColor = sides[0].ForegroundColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Quantity = sides[0].Quantity,
        };
        tx.Objects.Add(to);

        tx.Objects.Add(DuplicateFace(to, 127, 42, 0, sides[1]));
        tx.Objects.Add(DuplicateFace(to, 212, 42, 0, sides[2]));
        tx.Objects.Add(DuplicateFace(to, 42, 127, 0, sides[3]));
        tx.Objects.Add(DuplicateFace(to, 127, 127, 0, sides[4]));
        tx.Objects.Add(DuplicateFace(to, 212, 127, 0, sides[5]));

        return tx;
    }

    private TextureFactory.TextureDefinition D8TextureDefinition(
        QuickTextureField[] sides,
        Color color
    )
    {
        var font = new SystemFont();

        var tx = new TextureFactory.TextureDefinition
        {
            BackgroundColor = color,
            Height = 256,
            Width = 256,
            Shape = TextureFactory.TokenShape.Square,
        };

        var t0 = new TextureFactory.TextureObject
        {
            CenterX = 0,
            CenterY = 55,
            Scale = 0.8f,
            Font = font,
            Height = 110,
            Width = 110,
            Type = sides[0].FaceType,
            TriangleFace = true,
            RotationDegrees = 90,
            Text = sides[0].Caption,
            ForegroundColor = sides[0].ForegroundColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Quantity = sides[0].Quantity,
        };

        tx.Objects.Add(t0);
        tx.Objects.Add(DuplicateFace(t0, 0, 165, 90, sides[1]));
        tx.Objects.Add(DuplicateFace(t0, 127, 201, -90, sides[2]));
        tx.Objects.Add(DuplicateFace(t0, 127, 92, -90, sides[3]));
        tx.Objects.Add(DuplicateFace(t0, 127, 55, 90, sides[4]));
        tx.Objects.Add(DuplicateFace(t0, 127, 165, 90, sides[5]));
        tx.Objects.Add(DuplicateFace(t0, 255, 201, -90, sides[6]));
        tx.Objects.Add(DuplicateFace(t0, 255, 92, -90, sides[7]));

        return tx;
    }

    private TextureFactory.TextureDefinition D10TextureDefinition(
        QuickTextureField[] sides,
        Color color
    )
    {
        var font = new SystemFont();

        var tx = new TextureFactory.TextureDefinition
        {
            BackgroundColor = color,
            Height = 256,
            Width = 256,
            Shape = TextureFactory.TokenShape.Square,
        };

        var to = new TextureFactory.TextureObject
        {
            CenterX = 48,
            CenterY = 50,
            Scale = 0.8f,
            Font = font,
            Height = 71,
            Width = 49,
            Type = sides[0].FaceType,
            TriangleFace = false,
            RotationDegrees = 0,
            Text = sides[0].Caption,
            ForegroundColor = sides[0].ForegroundColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Quantity = sides[0].Quantity,
        };
        tx.Objects.Add(to);

        tx.Objects.Add(DuplicateFace(to, 128, 50, 0, sides[1]));
        tx.Objects.Add(DuplicateFace(to, 212, 50, 0, sides[2]));
        tx.Objects.Add(DuplicateFace(to, 169, 90, 180, sides[3]));
        tx.Objects.Add(DuplicateFace(to, 88, 90, 180, sides[4]));
        tx.Objects.Add(DuplicateFace(to, 48, 169, 0, sides[5]));
        tx.Objects.Add(DuplicateFace(to, 128, 169, 0, sides[6]));
        tx.Objects.Add(DuplicateFace(to, 212, 169, 0, sides[7]));
        tx.Objects.Add(DuplicateFace(to, 169, 266, 180, sides[8]));
        tx.Objects.Add(DuplicateFace(to, 88, 266, 180, sides[9]));

        return tx;
    }

    private TextureFactory.TextureDefinition D12TextureDefinition(
        QuickTextureField[] sides,
        Color color
    )
    {
        var font = new SystemFont();

        var tx = new TextureFactory.TextureDefinition
        {
            BackgroundColor = color,
            Height = 256,
            Width = 256,
            Shape = TextureFactory.TokenShape.Square,
        };

        var to = new TextureFactory.TextureObject
        {
            CenterX = 29,
            CenterY = 89,
            Scale = 0.8f,
            Font = font,
            Height = 53,
            Width = 47,
            Type = sides[0].FaceType,
            TriangleFace = false,
            RotationDegrees = 90,
            Text = sides[0].Caption,
            ForegroundColor = sides[0].ForegroundColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Quantity = sides[0].Quantity,
        };
        tx.Objects.Add(to);

        tx.Objects.Add(DuplicateFace(to, 29, 156, 90, sides[1]));
        tx.Objects.Add(DuplicateFace(to, 29, 223, 90, sides[2]));
        tx.Objects.Add(DuplicateFace(to, 93, 89, 90, sides[3]));
        tx.Objects.Add(DuplicateFace(to, 93, 156, 90, sides[4]));
        tx.Objects.Add(DuplicateFace(to, 93, 223, 90, sides[5]));
        tx.Objects.Add(DuplicateFace(to, 157, 89, 90, sides[6]));
        tx.Objects.Add(DuplicateFace(to, 157, 156, 90, sides[7]));
        tx.Objects.Add(DuplicateFace(to, 157, 223, 90, sides[8]));
        tx.Objects.Add(DuplicateFace(to, 221, 89, 90, sides[9]));
        tx.Objects.Add(DuplicateFace(to, 221, 156, 90, sides[10]));
        tx.Objects.Add(DuplicateFace(to, 221, 223, 90, sides[11]));

        return tx;
    }

    private TextureFactory.TextureDefinition D20TextureDefinition(
        QuickTextureField[] sides,
        Color color
    )
    {
        var font = new SystemFont();

        var tx = new TextureFactory.TextureDefinition
        {
            BackgroundColor = color,
            Height = 256,
            Width = 256,
            Shape = TextureFactory.TokenShape.Square,
        };

        var to = new TextureFactory.TextureObject
        {
            CenterX = 0,
            CenterY = 41,
            Scale = 0.8f,
            Font = font,
            Height = 82,
            Width = 82,
            Type = sides[0].FaceType,
            TriangleFace = true,
            RotationDegrees = 90,
            Text = sides[0].Caption,
            ForegroundColor = sides[0].ForegroundColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Quantity = sides[0].Quantity,
        };
        tx.Objects.Add(to);

        tx.Objects.Add(DuplicateFace(to, 64, 41, 90, sides[1]));
        tx.Objects.Add(DuplicateFace(to, 128, 41, 90, sides[2]));
        tx.Objects.Add(DuplicateFace(to, 192, 41, 90, sides[3]));
        tx.Objects.Add(DuplicateFace(to, 64, 82, -90, sides[4]));
        tx.Objects.Add(DuplicateFace(to, 128, 82, -90, sides[5]));
        tx.Objects.Add(DuplicateFace(to, 192, 82, -90, sides[6]));
        tx.Objects.Add(DuplicateFace(to, 255, 82, -90, sides[7]));
        tx.Objects.Add(DuplicateFace(to, 0, 123, 90, sides[8]));
        tx.Objects.Add(DuplicateFace(to, 64, 123, 90, sides[9]));
        tx.Objects.Add(DuplicateFace(to, 128, 123, 90, sides[10]));
        tx.Objects.Add(DuplicateFace(to, 192, 123, 90, sides[11]));

        tx.Objects.Add(DuplicateFace(to, 64, 164, -90, sides[12]));
        tx.Objects.Add(DuplicateFace(to, 128, 164, -90, sides[13]));

        tx.Objects.Add(DuplicateFace(to, 192, 164, -90, sides[14]));
        tx.Objects.Add(DuplicateFace(to, 255, 164, -90, sides[15]));

        tx.Objects.Add(DuplicateFace(to, 0, 205, 90, sides[16]));

        tx.Objects.Add(DuplicateFace(to, 64, 205, 90, sides[17]));
        tx.Objects.Add(DuplicateFace(to, 128, 205, 90, sides[18]));
        tx.Objects.Add(DuplicateFace(to, 192, 205, 90, sides[19]));

        return tx;
    }

    private TextureFactory.TextureObject DuplicateFace(
        TextureFactory.TextureObject obj,
        int centerX,
        int centerY,
        int rotation,
        QuickTextureField qtf
    )
    {
        TextureFactory.TextureObject tx = new()
        {
            CenterX = centerX,
            CenterY = centerY,
            Scale = obj.Scale,
            Font = obj.Font,
            Height = obj.Height,
            Width = obj.Width,
            Type = qtf.FaceType,
            RotationDegrees = rotation,
            Text = qtf.Caption,
            TriangleFace = obj.TriangleFace,
            ForegroundColor = qtf.ForegroundColor,
            Quantity = qtf.Quantity,
            HorizontalAlignment = obj.HorizontalAlignment,
            VerticalAlignment = obj.VerticalAlignment,
        };

        return tx;
    }
}

public sealed class DieParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Die;

    public float Size { get; set; }
    public Color Color { get; set; } = Colors.White;
    public QuickTextureField[] Sides { get; set; } = Array.Empty<QuickTextureField>();
    public int SideCount { get; set; }
    public VcToken.TokenBuildMode Mode { get; set; }
    public SnowportId FrontTemplate { get; set; }
    public SnowportId Dataset { get; set; }
}
