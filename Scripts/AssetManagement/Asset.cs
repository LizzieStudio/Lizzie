using System;
using Godot;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Represents a cloud-stored asset in the project
    /// </summary>
    public class Asset
    {
        
        public enum AssetType {Image, Spreadsheet, Mesh, Document}
        
        
        public AssetType Type { get; set; }

        /// <summary>
        /// Unique identifier for this asset
        /// </summary>
        public Guid AssetId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// User-defined name for the asset
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Original filename when uploaded
        /// </summary>
        public string OriginalFilename { get; set; }

        /// <summary>
        /// Cloud provider where this asset is stored
        /// </summary>
        public CloudProviderType ProviderType { get; set; }

        /// <summary>
        /// Cloud-specific identifier for retrieving the file
        /// (e.g., file ID, path, or URL)
        /// </summary>
        public string CloudFileId { get; set; }

        /// <summary>
        /// Cloud-specific path or location
        /// </summary>
        public string CloudPath { get; set; }

        /// <summary>
        /// File size in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// MIME type of the file
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// Date when the asset was uploaded
        /// </summary>
        public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Date when the asset was last modified
        /// </summary>
        public DateTime LastModifiedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Additional metadata specific to the cloud provider
        /// </summary>
        public string ProviderMetadata { get; set; }

        /// <summary>
        /// Cached image data for quick access (optional, can be null if not loaded)
        /// </summary>
        public Image Image { get; set; }


        /// <summary>
        /// We do not load the asset until it's needed, so this flag indicates whether we've downloaded it yet
        /// </summary>
        public bool AssetDownloaded { get; set; }
    }
}
