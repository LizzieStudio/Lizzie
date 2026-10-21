using System.Threading.Tasks;
using Godot;

namespace Lizzie;

/// <summary>
/// One card's picture, shared by every token and hand card that shows it.
/// </summary>
/// <remarks>
/// Its <see cref="Texture"/> and <see cref="Material"/> stay the same objects when it's redrawn,
/// so whatever shows them updates without being told.
/// </remarks>
public sealed class Face
{
    private static readonly StandardMaterial3D TokenMaterial = GD.Load<StandardMaterial3D>(
        "res://Materials/token_face.tres"
    );

    private static readonly Texture2D MissingTexture = GD.Load<Texture2D>(
        "res://Textures/missing_frame.png"
    );

    /// <summary>
    /// Shows white, for a side that has nothing to show yet.
    /// </summary>
    public static Face Blank { get; } = new(White(), null);

    /// <summary>
    /// Shown in place of a frame or image that doesn't exist.
    /// </summary>
    public static Face Missing { get; } = new(MissingTexture, null);

    private readonly TaskCompletionSource _shown = new();

    /// <summary>
    /// The set that holds this face, which its holders release, or null for one that's always kept.
    /// </summary>
    public FaceSet Set { get; }

    public Texture2D Texture { get; }

    /// <summary>
    /// The material to show this face on a token.
    /// </summary>
    public StandardMaterial3D Material { get; }

    /// <summary>
    /// Completes once the face shows its first picture.
    /// </summary>
    public Task Loaded => _shown.Task;

    /// <summary>
    /// A face of <paramref name="set"/> that shows nothing until <see cref="Show"/> is called.
    /// </summary>
    public Face(FaceSet set)
        : this(new ImageTexture(), set) { }

    private Face(Texture2D texture, FaceSet set)
    {
        Texture = texture;
        Set = set;
        Material = (StandardMaterial3D)TokenMaterial.Duplicate();
        Material.AlbedoTexture = texture;
        if (set == null)
            _shown.SetResult();
    }

    private static ImageTexture White()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Shows <paramref name="image"/>, generating its mipmaps off the main thread, or the checkerboard for null.
    /// </summary>
    public async Task Show(Image image)
    {
        if (image == null)
            image = MissingTexture.GetImage();
        else
            await Task.Run(() => image.GenerateMipmaps());
        ((ImageTexture)Texture).SetImage(image);
        _shown.TrySetResult();
    }
}
