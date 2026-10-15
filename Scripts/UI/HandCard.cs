using System;
using Godot;

/// <summary>
/// Shows one card in a hand.
/// </summary>
public partial class HandCard : PanelContainer
{
    // An empty panel with room around the card for the selection outline.
    private static readonly Theme OutlineRoom = MakeTheme();

    private static Theme MakeTheme()
    {
        var room = new StyleBoxEmpty();
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
            room.SetContentMargin(side, SelectionHighlight.OutlineWidth);
        var theme = new Theme();
        theme.SetStylebox("panel", nameof(PanelContainer), room);
        return theme;
    }

    private SnowTag _reference;

    public SnowTag Reference
    {
        get => _reference;
        set
        {
            _reference = value;
            RecordService.Instance.QueueSync(this);
        }
    }

    /// <remarks>
    /// Used when the card is in an opponent's hand.
    /// </remarks>
    public bool AlwaysShowBack { get; set; }

    /// <summary>
    /// The card's size in cm.
    /// </summary>
    public Vector2 TableSize { get; private set; } = HandRow.TableSize(null);

    /// <summary>
    /// The card's turn about Y in the hand, in radians.
    /// </summary>
    public float Yaw { get; private set; }

    private CardFace _face;

    public override void _Ready()
    {
        // Built in code, since its room comes from the outline's width.
        Theme = OutlineRoom;
        _face = GetNode<CardFace>("Face");
    }

    public override void _EnterTree()
    {
        RecordService.Instance.Watch(this, Sync);
        RecordService.Instance.Watch(this, SyncSelection);
    }

    private void Sync(IRecordReader R)
    {
        var state = R.Get<ComponentState>(Reference);
        var prototype = state == null ? null : R.GetIncludingDeleted<Prototype>(state.PrototypeRef);
        Yaw = state?.Rotation.Y ?? 0;
        TableSize = HandRow.TableSize(prototype?.Parameters as PrintedParameters);
        _face.ShowCard(state, state != null && (AlwaysShowBack || IsFaceDown(state)), R);
    }

    private void SyncSelection(IRecordReader R)
    {
        var target = new RecordTarget(Reference);
        var colors = R.SelectionColors(s =>
            s.Within == SnowTag.Empty && s.Targets.Contains(target)
        );
        SelectionHighlight.Outline(this, "panel", colors, target);
    }

    public static bool IsFaceDown(ComponentState s) => Mathf.Cos(s.Rotation.Z) < 0;

    public static float ScreenRotation(float yaw) => -yaw;

    public static float CameraYaw(Camera3D camera)
    {
        var across = camera.GlobalBasis.X;
        return Mathf.Atan2(-across.Z, across.X);
    }

    /// <summary>
    /// A held card's record turned <paramref name="angle"/> about Y, along with its offset from what holds it.
    /// </summary>
    public static ComponentState Turned(ComponentState s, float angle) =>
        s with
        {
            Rotation = s.Rotation with { Y = s.Rotation.Y + angle },
            Position = new Basis(Vector3.Up, angle) * s.PositionAt(0),
        };
}
