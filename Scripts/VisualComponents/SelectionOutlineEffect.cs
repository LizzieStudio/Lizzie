using System;
using Godot;
using Godot.Collections;

/// <summary>
/// <para>
/// Draws <see cref="SelectionOutline"/>'s outlines in one compute pass over the table's view,
/// after its transparent objects.
/// </para>
///
/// <para>
/// Runs on the render thread. <see cref="SelectionOutline"/> sets its fields on the main thread,
/// replacing each whole, so the render thread always reads a complete one.
/// </para>
/// </summary>
public partial class SelectionOutlineEffect : CompositorEffect
{
    /// <summary>The widest outline the shader can reach, in pixels.</summary>
    public const float MaxWidth = 8;

    // The shader's tile size on each side.
    private const uint Tile = 16;

    /// <summary>The palette's size in the shader.</summary>
    public const int PaletteSize = 32;

    /// <summary>The mask texture to outline, the size of the table's view.</summary>
    public Rid Mask;

    /// <summary>The outline colors, indexed by the mask.</summary>
    public Color[] Palette = new Color[PaletteSize];

    /// <summary>The outline's width in pixels, including the dark rim on each side.</summary>
    public float Width = 5;

    private RenderingDevice _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _sampler;
    private Rid _palette;

    public SelectionOutlineEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        var spirv = GD.Load<RDShaderFile>("res://Shaders/selection_outline.glsl").GetSpirV();
        RenderingServer.CallOnRenderThread(Callable.From(() => Create(spirv)));
    }

    private void Create(RDShaderSpirV spirv)
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null)
            return;
        if (spirv.CompileErrorCompute != "")
        {
            GD.PushError(
                $"The selection outline's compute shader failed: {spirv.CompileErrorCompute}"
            );
            return;
        }
        _shader = _rd.ShaderCreateFromSpirV(spirv);
        _pipeline = _rd.ComputePipelineCreate(_shader);
        _sampler = _rd.SamplerCreate(new RDSamplerState());
        _palette = _rd.UniformBufferCreate(PaletteSize * 16);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _rd == null)
            return;
        // Freeing the shader frees the pipeline with it.
        foreach (var rid in new[] { _shader, _sampler, _palette })
            if (rid.IsValid)
                _rd.FreeRid(rid);
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (
            !_pipeline.IsValid
            || renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers
        )
            return;
        var size = buffers.GetInternalSize();
        // The camera as drawn, to read its depth buffer.
        var inverse = renderData.GetRenderSceneData().GetCamProjection().Inverse();
        var mask = RenderingServer.TextureGetRdTexture(Mask);
        if (size.X == 0 || size.Y == 0 || !mask.IsValid)
            return;

        var colors = new float[PaletteSize * 4];
        for (var i = 0; i < PaletteSize; i++)
        {
            colors[i * 4] = Palette[i].R;
            colors[i * 4 + 1] = Palette[i].G;
            colors[i * 4 + 2] = Palette[i].B;
            colors[i * 4 + 3] = Palette[i].A;
        }
        var paletteBytes = new byte[colors.Length * sizeof(float)];
        Buffer.BlockCopy(colors, 0, paletteBytes, 0, paletteBytes.Length);
        _rd.BufferUpdate(_palette, 0, (uint)paletteBytes.Length, paletteBytes);

        // The shader's Params, in order.
        float[] values =
        [
            .. Column(inverse.X),
            .. Column(inverse.Y),
            .. Column(inverse.Z),
            .. Column(inverse.W),
            0,
            0,
            0,
            0.75f, // rim_color
            BitConverter.Int32BitsToSingle(size.X),
            BitConverter.Int32BitsToSingle(size.Y),
            Math.Min(Width, MaxWidth),
            1, // rim
            0.01f, // bias
        ];
        var pushConstant = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, pushConstant, 0, pushConstant.Length);

        for (uint view = 0; view < buffers.GetViewCount(); view++)
        {
            var uniforms = new Array<RDUniform>
            {
                Uniform(0, RenderingDevice.UniformType.SamplerWithTexture, _sampler, mask),
                Uniform(
                    1,
                    RenderingDevice.UniformType.SamplerWithTexture,
                    _sampler,
                    buffers.GetDepthLayer(view)
                ),
                Uniform(2, RenderingDevice.UniformType.Image, buffers.GetColorLayer(view)),
                Uniform(3, RenderingDevice.UniformType.UniformBuffer, _palette),
            };
            var set = UniformSetCacheRD.GetCache(_shader, 0, uniforms);

            var list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, set, 0);
            _rd.ComputeListSetPushConstant(list, pushConstant, (uint)pushConstant.Length);
            _rd.ComputeListDispatch(
                list,
                ((uint)size.X + Tile - 1) / Tile,
                ((uint)size.Y + Tile - 1) / Tile,
                1
            );
            _rd.ComputeListEnd();
        }
    }

    private static float[] Column(Vector4 column) => [column.X, column.Y, column.Z, column.W];

    private static RDUniform Uniform(
        int binding,
        RenderingDevice.UniformType type,
        params Rid[] ids
    )
    {
        var uniform = new RDUniform { Binding = binding, UniformType = type };
        foreach (var id in ids)
            uniform.AddId(id);
        return uniform;
    }
}
