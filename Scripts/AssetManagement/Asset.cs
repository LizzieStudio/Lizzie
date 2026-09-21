using System;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Represents a cloud-stored asset in the project
    /// </summary>
    public record Asset : Replicated
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
        /// User-defined name for the asset
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Cloud-specific path or location
        /// </summary>
        public string CloudPath { get; init; }
    }
}
