namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Represents a cloud-stored image in the project
    /// </summary>
    [JsonName("ImageAsset")]
    public record ImageAsset : Replicated
    {
        /// <summary>
        /// User-defined name for the image
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Cloud-specific path
        /// </summary>
        public string CloudPath { get; init; } = "";

        /// <summary>
        /// How an image divides into frames, like a sheet of cards.
        /// </summary>
        public GridLayout Layout { get; init; } = GridLayout.Single;
    }

    public readonly record struct GridLayout(int Columns, int Rows, int? Count = null)
    {
        /// <summary>
        /// A whole image as one frame.
        /// </summary>
        public static GridLayout Single => new(1, 1);

        /// <summary>
        /// The number of frames: Count, or Columns * Rows when Count is missing or larger.
        /// </summary>
        public int FrameCount() => Count is int c && c < Columns * Rows ? c : Columns * Rows;

        /// <summary>
        /// The 0-based frame index that a 1-based sequence value names, or null when there is no such frame.
        /// </summary>
        public int? Frame(int value) => value >= 1 && value <= FrameCount() ? value - 1 : null;

        /// <summary>
        /// Parses which frames to use with <see cref="SequenceExpressions.TryParseNumbers"/>.
        /// A blank expression means every frame, up to <see cref="SequenceExpressions.MaxValues"/>.
        /// </summary>
        public bool TryParse(string expression, out int[] values, int? count = null) =>
            SequenceExpressions.TryParseNumbers(
                string.IsNullOrWhiteSpace(expression)
                    ? $"1-{System.Math.Min(FrameCount(), SequenceExpressions.MaxValues)}"
                    : expression,
                out values,
                count
            );
    }
}
