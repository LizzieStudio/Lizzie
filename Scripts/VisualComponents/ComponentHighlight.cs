using System.Collections.Generic;
using Godot;

namespace Lizzie;

/// <summary>
/// <para>Draws screen-space outlines around components, and paints components with stripes.</para>
///
/// <para>
/// Components put copies of their meshes on <see cref="MaskLayer"/>,
/// which a second camera renders into a mask of codes and depths.
/// <see cref="ComponentHighlightEffect"/> then draws a constant-width outline around the mask's visible pixels
/// and paints the visible pixels marked for paint, in a compute pass on the table camera,
/// so overlapping components share one outline around their union,
/// and outlines and paint hide behind other objects.
/// </para>
/// </summary>
[GlobalClass]
[Icon("res://Textures/Editor/ComponentHighlight.svg")]
public partial class ComponentHighlight : Node
{
    /// <summary>
    /// For profiling, skip all work. You can set it with the debug console's /highlights.
    /// </summary>
    public static bool Enabled = true;

    /// <summary>
    /// <para>The render layer only the mask camera sees.</para>
    ///
    /// This is the 20th layer. The last one.
    /// </summary>
    public const uint MaskLayer = 1u << 19;

    /// <summary>
    /// The outline's width in pixels, including the dark rim on each side,
    /// up to <see cref="ComponentHighlightEffect.MaxWidth"/>.
    /// </summary>
    // The range's maximum is ComponentHighlightEffect.MaxWidth.
    [Export(PropertyHint.Range, "3,8,0.2,suffix:px")]
    public float Width = 5f;

    private static readonly Shader MaskShader = GD.Load<Shader>(
        "res://Shaders/component_highlight_mask.gdshader"
    );

    // The outline colors in use, by palette slot; the mask stores slot indices.
    private static readonly Color?[] Palette = new Color?[ComponentHighlightEffect.PaletteSize];

    /// <summary>
    /// The paint's color, in opaque stripes with black that hide the component, so it's easy to find.
    /// </summary>
    public static readonly Color PaintColor = Colors.Red;

    /// <summary>
    /// How fast the paint's stripes move, in pixels per second.
    /// </summary>
    public const float StripeSpeed = 10;

    // A mask code's slot for no outline, after the palette's slots.
    private const int NoOutline = ComponentHighlightEffect.PaletteSize;

    // Added to a mask code to paint the component. The shader reads the same flag.
    private const int Painted = 64;

    // Each mask code's material, made the first time the code is used.
    private static readonly Dictionary<int, ShaderMaterial> Materials = new();

    // Each color in use: its slot, and how many components hold it.
    private static readonly Dictionary<Color, (int Slot, int Holders)> InUse = new();

    private SubViewport _mask;
    private Camera3D _maskCamera;
    private ComponentHighlightEffect _effect;
    private Compositor _compositor;

    // We compare this with Enabled to determine when to toggle.
    private bool _running = true;

    /// <summary>
    /// The material that draws a component into the mask with the given <paramref name="outline"/> color,
    /// or none, and <paramref name="painted"/> or not.
    /// The outline color keeps its palette slot until every holder calls <see cref="Release"/>.
    /// </summary>
    public static Material Reserve(Color? outline, bool painted)
    {
        int code = (outline is Color color ? Hold(color) : NoOutline) + (painted ? Painted : 0);
        if (!Materials.TryGetValue(code, out var material))
        {
            Materials[code] = material = new ShaderMaterial { Shader = MaskShader };
            material.SetShaderParameter("code", (float)code);
        }
        return material;
    }

    // The palette slot of an outline color, holding it once more.
    private static int Hold(Color color)
    {
        if (InUse.TryGetValue(color, out var use))
        {
            InUse[color] = (use.Slot, use.Holders + 1);
            return use.Slot;
        }

        var slot = System.Array.IndexOf(Palette, null);
        if (slot < 0)
        {
            // The hover colors and one per seat never fill the palette, so this is a leak.
            GD.PushError(
                $"The outline's palette is full ({Palette.Length} colors), so {color} is drawn in another's color."
            );
            return Palette.Length - 1;
        }

        Palette[slot] = color;
        InUse[color] = (slot, 1);
        return slot;
    }

    /// <summary>Lets go of a color from <see cref="Reserve"/>, freeing its slot when nothing holds it.</summary>
    public static void Release(Color color)
    {
        // A color that didn't fit was never held.
        if (!InUse.TryGetValue(color, out var use))
            return;
        if (use.Holders > 1)
        {
            InUse[color] = (use.Slot, use.Holders - 1);
            return;
        }
        InUse.Remove(color);
        Palette[use.Slot] = null;
    }

    public override void _Ready()
    {
        // Shares the view's world, but sees only the mask layer.
        // HDR keeps the indices and depths exact.
        _mask = new SubViewport
        {
            TransparentBg = true,
            UseHdr2D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            PositionalShadowAtlasSize = 0,
            GuiDisableInput = true,
        };
        AddChild(_mask);

        _maskCamera = new Camera3D
        {
            CullMask = MaskLayer,
            Current = true,
            // The view's environment would change the mask's values.
            Environment = new Environment { BackgroundMode = Environment.BGMode.ClearColor },
        };
        _mask.AddChild(_maskCamera);

        _effect = new ComponentHighlightEffect { Mask = _mask.GetTexture().GetRid() };
        _compositor = new Compositor
        {
            CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _effect },
        };

        // Follows the camera just before drawing, after everything has moved.
        RenderingServer.FramePreDraw += SyncCamera;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= SyncCamera;
    }

    private void SyncCamera()
    {
        var viewport = GetViewport();
        var camera = viewport?.GetCamera3D();
        if (camera == null)
            return;

        // The view's camera never sees the mask copies, and runs the compute pass.
        camera.CullMask &= ~MaskLayer;
        if (camera.Compositor != _compositor)
            camera.Compositor = _compositor;

        if (_running != Enabled)
        {
            _running = Enabled;
            _mask.RenderTargetUpdateMode = Enabled
                ? SubViewport.UpdateMode.Always
                : SubViewport.UpdateMode.Disabled;
            _effect.Enabled = Enabled;
        }
        if (!Enabled)
            return;

        // The compute pass reads the mask pixel for pixel.
        var size = (Vector2I)viewport.GetVisibleRect().Size;
        if (_mask.Size != size)
            _mask.Size = size;

        _maskCamera.GlobalTransform = camera.GlobalTransform;
        _maskCamera.Projection = camera.Projection;
        _maskCamera.Fov = camera.Fov;
        _maskCamera.Size = camera.Size;
        _maskCamera.Near = camera.Near;
        _maskCamera.Far = camera.Far;
        _maskCamera.KeepAspect = camera.KeepAspect;
        _maskCamera.HOffset = camera.HOffset;
        _maskCamera.VOffset = camera.VOffset;

        var palette = new Color[ComponentHighlightEffect.PaletteSize];
        for (var i = 0; i < Palette.Length; i++)
            palette[i] = Palette[i] ?? default;
        _effect.Palette = palette;
        _effect.Width = Width;
        _effect.Shift = (int)(Time.GetTicksMsec() * StripeSpeed / 1000);
    }
}
