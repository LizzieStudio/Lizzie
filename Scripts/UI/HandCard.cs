using Godot;

namespace Lizzie;

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

    private StageKey<ComponentPosition> _reference;

    /// <summary>
    /// The card's component atom.
    /// </summary>
    public StageKey<ComponentPosition> Reference
    {
        get => _reference;
        set
        {
            _reference = value;
            Atoms.R.QueueSync(this);
        }
    }

    /// <summary>
    /// The card's size in cm.
    /// </summary>
    public Vector2 TableSize { get; private set; } = HandRow.TableSize(null);

    /// <summary>
    /// The card's turn about Y in the hand, in radians.
    /// </summary>
    public float Yaw { get; private set; }

    private CardFace _face;

    // Paints the card when a reset would delete it, like ComponentHighlight does on the table.
    [Export]
    private ColorRect _paint;

    public override void _Ready()
    {
        // Built in code, since its room comes from the outline's width.
        Theme = OutlineRoom;
        _face = GetNode<CardFace>("Face");
        _paint.Color = ComponentHighlight.PaintColor;
        ((ShaderMaterial)_paint.Material).SetShaderParameter(
            "speed",
            ComponentHighlight.StripeSpeed
        );
    }

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
        Atoms.R.Watch(this, SyncSelection);
    }

    private void Sync(IWatchReader R)
    {
        var state = R.Get(ComponentPosition.ById, Reference);
        var prototype = state == null ? null : R.GetPrototype(state);
        Yaw = state?.Rotation.Y ?? 0;
        TableSize = HandRow.TableSize(prototype?.Parameters as PrintedParameters);
        _face.ShowCard(
            state,
            state != null && GetParent<HandRow>().FacesAway != state.IsFaceDown,
            R
        );
    }

    private void SyncSelection(IWatchReader R)
    {
        var colors = R.SelectionColors(ViewPlace.Of<GameObjects>(Reference.StageRef));
        SelectionHighlight.Outline(this, "panel", colors, Reference);
        _paint.Visible = ProjectService.Instance.GameObjects.IsMarkedForReset(R, Reference);
    }

    public static float ScreenRotation(float yaw) => -yaw;

    public static float CameraYaw(Camera3D camera)
    {
        var across = camera.GlobalBasis.X;
        return Mathf.Atan2(-across.Z, across.X);
    }

    /// <summary>
    /// A held card's atom turned <paramref name="angle"/> about Y, along with its offset from what holds it.
    /// </summary>
    public static ComponentPosition Turned(ComponentPosition s, float angle) =>
        s with
        {
            Rotation = s.Rotation with { Y = s.Rotation.Y + angle },
            Position = new Basis(Vector3.Up, angle) * s.PositionAt(0),
        };
}
