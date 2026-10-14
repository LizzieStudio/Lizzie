using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

/// <summary>
/// A grid image for tokens. Used to show downloaded images and local renders.
/// </summary>
/// <remarks>
/// You have to manually call <c>Acquire</c> once per face, and <see cref="Release"/> when the face is done with it.
/// </remarks>
public sealed class Sheet
{
    private static readonly Shader FaceShader = GD.Load<Shader>(
        "res://Shaders/token_face.gdshader"
    );

    private static readonly Dictionary<string, Sheet> Sheets = new();

    /// <summary>
    /// Shows white, for a face that has nothing to show yet.
    /// </summary>
    public static Sheet Blank { get; } = new(null, GridLayout.Single);

    /// <summary>
    /// Shown in place of a frame or image that doesn't exist.
    /// </summary>
    public static Sheet Missing { get; } =
        new(null, GridLayout.Single, GD.Load<Texture2D>("res://Textures/missing_frame.png"));

    private readonly string _key;
    private readonly CancellationTokenSource _cancel = new();
    private int _faces;
    private Func<CancellationToken, Task<Texture2D>> _build;
    private Task _loaded;
    private SheetDrawing _drawing;

    public GridLayout Layout { get; private set; }

    /// <summary>
    /// The material to show this sheet.
    /// </summary>
    public ShaderMaterial Material { get; } = new() { Shader = FaceShader };

    /// <summary>
    /// The image, or null if it isn't done loading.
    /// </summary>
    public Texture2D Texture { get; private set; }

    /// <summary>
    /// Invoked when <see cref="Texture"/> or <see cref="Layout"/> changes.
    /// </summary>
    public event Action Changed;

    private Sheet(string key, GridLayout layout, Texture2D texture = null)
    {
        _key = key;
        Show(texture, layout);
        if (key == null)
            _loaded = Task.CompletedTask;
    }

    /// <summary>
    /// Holds the image sheet under <paramref name="key"/>, creating it with <paramref name="build"/> if it hasn't been already.
    /// </summary>
    /// <param name="build">Makes the image, once <see cref="Load"/> is first called. If it's canceled, it can return null.</param>
    public static Sheet Acquire(
        string key,
        GridLayout layout,
        Func<CancellationToken, Task<Texture2D>> build
    ) => Hold(key, () => new Sheet(key, layout) { _build = build });

    /// <summary>
    /// Holds the drawn sheet under <paramref name="key"/>, creating it with <paramref name="build"/> if it hasn't been already.
    /// Faces pick their frame with <see cref="Frame"/>.
    /// </summary>
    /// <param name="source">Reads what the sheet shows. The sheet redraws when what it read changes.</param>
    public static Sheet Acquire(
        string key,
        TextureFactory factory,
        Func<IRecordReader, Drawing> source
    ) =>
        Hold(
            key,
            () =>
            {
                var sheet = new Sheet(key, GridLayout.Single);
                sheet._drawing = new SheetDrawing
                {
                    Sheet = sheet,
                    Factory = factory,
                    Source = source,
                    Token = sheet._cancel.Token,
                };
                factory.Drawings.AddChild(sheet._drawing);
                return sheet;
            }
        );

    private static Sheet Hold(string key, Func<Sheet> create)
    {
        if (!Sheets.TryGetValue(key, out var sheet))
            Sheets[key] = sheet = create();
        sheet._faces++;
        return sheet;
    }

    /// <summary>
    /// Gets the frame of a drawn sheet that shows <paramref name="row"/>.
    /// </summary>
    public int Frame(SnowTag row) => _drawing.Frame(row);

    /// <summary>
    /// Starts building the image if it hasn't started.
    /// </summary>
    /// <returns>
    /// Completes when <see cref="Texture"/> is set or when the sheet is dropped.
    /// For a drawn sheet, when every frame faces asked for is drawn.
    /// </returns>
    public Task Load() => _drawing?.Loaded() ?? (_loaded ??= Build());

    private async Task Build()
    {
        var build = _build;
        _build = null;
        Texture2D texture = null;
        try
        {
            texture = await build(_cancel.Token);
        }
        catch (Exception e)
        {
            GD.PrintErr(e);
        }
        if (_cancel.IsCancellationRequested)
            return;
        Show(texture ?? Missing.Texture, Layout);
        // Faces made after this try again, say once a failed download can succeed.
        if (texture == null && Sheets.GetValueOrDefault(_key) == this)
            Sheets.Remove(_key);
    }

    /// <summary>
    /// Shows <paramref name="texture"/>, a grid with <paramref name="layout"/>.
    /// </summary>
    public void Show(Texture2D texture, GridLayout layout)
    {
        Texture = texture;
        Layout = layout;
        Material.SetShaderParameter("albedo_texture", texture);
        Material.SetShaderParameter(
            "grid",
            new Vector2I(Math.Max(layout.Columns, 1), Math.Max(layout.Rows, 1))
        );
        Changed?.Invoke();
    }

    /// <summary>
    /// Lets go of a sheet held with <see cref="Acquire"/>.
    /// The last face to let go drops it and cancels its build.
    /// </summary>
    public void Release()
    {
        if (_key == null || --_faces > 0)
            return;
        _cancel.Cancel();
        if (GodotObject.IsInstanceValid(_drawing))
            _drawing.QueueFree();
        if (Sheets.GetValueOrDefault(_key) == this)
            Sheets.Remove(_key);
    }

    /// <summary>
    /// Clears every sheet. Faces still holding one keep it.
    /// </summary>
    public static void Clear() => Sheets.Clear();
}

/// <summary>
/// What one side of a token shows.
/// </summary>
public readonly record struct Face(Sheet Sheet, int Frame)
{
    /// <summary>
    /// Shows white.
    /// </summary>
    public static Face Blank => new(Sheet.Blank, 0);
}
