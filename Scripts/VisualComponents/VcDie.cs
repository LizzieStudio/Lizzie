using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

public partial class VcDie : VisualComponentBase
{
    [Export]
    private int _sides;

    /// <summary>The rotation in degrees that shows each face up, face 1 first.</summary>
    [Export]
    private Vector3[] _sideRotations;

    private MeshInstance3D _mainMesh;
    private readonly FaceLoader _faces;

    public VcDie() => _faces = new FaceLoader(ShowNet);

    public override void _Ready()
    {
        base._Ready();

        _mainMesh = GetNode<MeshInstance3D>("ObjectMesh");

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

    // each die scene's face rotations, by scene path
    private static readonly Dictionary<string, Vector3[]> FacesByScene = new();

    /// <summary>
    /// The rotation in degrees that shows each face up, face 1 first, read from the die's scene.
    /// </summary>
    public static Vector3[] FaceRotations(DieParameters p, IAtomReader R)
    {
        var path = Utility.DieScene(p, R);
        if (FacesByScene.TryGetValue(path, out var faces))
            return faces;

        // Read from the scene's root without instancing it.
        faces = [];
        var state = GD.Load<PackedScene>(path).GetState();
        for (int i = 0; i < state.GetNodePropertyCount(0); i++)
            if (state.GetNodePropertyName(0, i) == PropertyName._sideRotations)
                faces = state.GetNodePropertyValue(0, i).AsVector3Array();
        return FacesByScene[path] = faces;
    }

    /// <summary>The rotation that shows <paramref name="face"/> up, counting from 1, or null if there's no such face.</summary>
    public static Vector3? FaceRotation(DieParameters p, int face, IAtomReader R) =>
        FaceRotations(p, R) is var faces && face >= 1 && face <= faces.Length
            ? faces[face - 1] * (3.14159f / 180f) //convert to radians
            : null;

    public override float MaxAxisSize => Scale.X;
    public override GeometryInstance3D DragMesh => _mainMesh;

    public override bool PlayTransition(
        ComponentPosition s,
        CommandName? writtenBy,
        long MsecSinceStart
    )
    {
        if (writtenBy != DieCommands.Roll.Name || MsecSinceStart >= _rollDuration * 1000)
            return false;

        _rollTargetRotation = s.Rotation;
        _rollInProcess = true;
        _rollTime = Math.Max(MsecSinceStart, 0) / 1000.0;
        return true;
    }

    protected override void Setup(ComponentParameters parameters, IAtomReader R)
    {
        base.Setup(parameters, R);
        Apply((DieParameters)parameters, R);
    }

    /// <summary>
    /// Sizes the die from its parameters and loads its net.
    /// </summary>
    private void Apply(DieParameters p, IAtomReader R)
    {
        _mainMesh = GetNode<MeshInstance3D>("ObjectMesh");

        if (p.Size <= 0)
            return;

        float size = p.Size / 10f;

        YHeight = size;

        Scale = new Vector3(size, size, size);

        _faces.Load(GetPrototype(R), Item, TextureFactory, R);
    }

    private void ShowNet() => _mainMesh.MaterialOverride = _faces.Front.Material;

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationPredelete)
            _faces.Drop();
    }
}

[JsonName("DieParameters")]
public sealed record DieParameters : ComponentParameters
{
    public static readonly ImmutableSortedDictionary<int, string> Scenes = new Dictionary<
        int,
        string
    >
    {
        [6] = "res://Scenes/VisualComponents/Dice/VcD6s.tscn",
        [8] = "res://Scenes/VisualComponents/Dice/VcD8.tscn",
        [10] = "res://Scenes/VisualComponents/Dice/VcD10.tscn",
        [12] = "res://Scenes/VisualComponents/Dice/VcD12.tscn",
        [20] = "res://Scenes/VisualComponents/Dice/VcD20.tscn",
    }.ToImmutableSortedDictionary();

    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Die;

    public float Size { get; init; }
    public Color Color { get; init; } = Colors.White;

    /// <summary>
    /// The number of sides set by the user. If they pick "Auto", set it to 0.
    /// </summary>
    public int SideCount { get; init; }

    /// <summary>
    /// What the faces show. Either a <see cref="QuickSide"/> or a <see cref="TemplateSide"/>.
    /// </summary>
    public SideParameters Faces { get; init; } = new QuickSide();

    /// <summary>
    /// The number of sides the die has, and the error when there is one.
    /// </summary>
    public (int Sides, string Error) Resolve(IAtomReader R)
    {
        if (SideCount > 0 && !Scenes.ContainsKey(SideCount))
            return (6, $"A die can't have {SideCount} sides.");

        if (Faces is TemplateSide side)
        {
            int net = R.Get(Template.ById, side.Template)?.Sides ?? 0;
            if (SideCount > 0)
                return net == 0 || net == SideCount
                    ? (SideCount, null)
                    : (SideCount, $"The template is for a d{net}, not a d{SideCount}.");
            return (Scenes.ContainsKey(net) ? net : 6, null);
        }

        var (fewest, unbounded, ok) = SequenceExpressions.Measure(((QuickSide)Faces).Expressions());
        string error = ok
            ? null
            : "The values of a suit have an error, or more than one suit uses * to fill the die.";
        if (SideCount > 0)
            return fewest > SideCount
                ? (
                    SideCount,
                    $"The suits make {fewest} faces, more than the die's {SideCount} sides."
                )
                : (SideCount, error);
        int most = Scenes.Keys.Last();
        if (fewest > most)
            return (6, $"The suits make {fewest} faces, but a die has at most {most} sides.");

        // An unbounded suit grows to fill the die, so any die with room will do.
        foreach (int sides in Scenes.Keys)
            if (unbounded ? sides >= fewest : sides % Math.Max(fewest, 1) == 0)
                return (sides, error);
        var larger = string.Join(", ", Scenes.Keys.Where(s => s > fewest));
        return (
            6,
            $"The suits make {fewest} faces, which no die can repeat evenly. Make {larger} faces."
        );
    }
}
