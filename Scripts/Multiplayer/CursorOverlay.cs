using Godot;

/// <summary>
/// Draws the player cursors in a 2D overlay.
/// </summary>
[Icon("res://Textures/Editor/CursorOverlay.svg")]
public partial class CursorOverlay : Control
{
    private static readonly Texture2D Texture = GD.Load<Texture2D>("res://Textures/cursor.png");

    // The cursor's height as a fraction of the screen height.
    private const float CursorHeight = 0.02f;

    private static RecordService R => RecordService.Instance;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var cursors = PresenceSynchronizer.Instance;
        var camera = GetViewport().GetCamera3D();
        if (cursors == null || camera == null)
            return;

        var size =
            Texture.GetSize() * (GetViewportRect().Size.Y * CursorHeight / Texture.GetHeight());
        foreach (var (source, pos) in cursors.RemoteCursors)
        {
            if (camera.IsPositionBehind(pos))
                continue;
            var tip = camera.UnprojectPosition(pos);
            DrawTextureRect(Texture, new Rect2(tip, size), false, R.SeatColor(source));
        }
    }
}
