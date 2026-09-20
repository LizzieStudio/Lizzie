using System;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Represents a cloud-stored asset in the project
    /// </summary>
    public record Asset : IReplicated
    {
        public enum AssetType
        {
            Image,
            Spreadsheet,
            Mesh,
            Document,
        }

        public AssetType Type { get; init; }

        /// <summary>
        /// Unique identifier for this asset
        /// </summary>
        public SnowTag Id { get; init; }

        /// <summary>
        /// Reversible soft-delete flag.
        /// </summary>
        public bool Deleted { get; init; }

        /// <summary>The id of the last event that updated this asset.</summary>
        public SnowportId LastUpdateId { get; init; }

        /// <summary>
        /// User-defined name for the asset
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Cloud-specific path or location
        /// </summary>
        public string CloudPath { get; init; }

        public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
            this with
            {
                Id = id,
                LastUpdateId = lastUpdateId,
            };
    }
}
