using System;
using System.IO;
using System.Threading.Tasks;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Interface for cloud storage providers
    /// </summary>
    public interface ICloudProvider
    {
        /// <summary>
        /// Initialize the provider with credentials and base folder URL
        /// </summary>
        /// <param name="baseFolderUrl">Base folder URL or path in the cloud storage</param>
        /// <param name="credentials">Provider-specific credentials (JSON, tokens, etc.)</param>
        Task InitializeAsync(string baseFolderUrl, string credentials);

        /// <summary>
        /// Upload a file to the cloud storage
        /// </summary>
        /// <param name="localFilePath">Path to the local file to upload</param>
        /// <param name="destinationPath">Destination path/name in cloud storage</param>
        /// <returns>Cloud file identifier and metadata</returns>
        Task<CloudFileInfo> UploadFileAsync(string localFilePath, string destinationPath);

        /// <summary>
        /// Upload a file from a stream to the cloud storage
        /// </summary>
        /// <param name="stream">Stream containing the file data</param>
        /// <param name="filename">Name for the file in cloud storage</param>
        /// <param name="destinationPath">Destination path in cloud storage</param>
        /// <returns>Cloud file identifier and metadata</returns>
        Task<CloudFileInfo> UploadStreamAsync(Stream stream, string filename, string destinationPath);

        /// <summary>
        /// Download a file from cloud storage
        /// </summary>
        /// <param name="cloudFileId">Cloud file identifier</param>
        /// <param name="localFilePath">Local path where file should be saved</param>
        Task DownloadFileAsync(string cloudFileId, string localFilePath);

        /// <summary>
        /// Download a file to a stream
        /// </summary>
        /// <param name="cloudFileId">Cloud file identifier</param>
        /// <returns>Stream containing the file data</returns>
        Task<Stream> DownloadStreamAsync(string cloudFileId);

        /// <summary>
        /// Delete a file from cloud storage
        /// </summary>
        /// <param name="cloudFileId">Cloud file identifier</param>
        Task DeleteFileAsync(string cloudFileId);

        /// <summary>
        /// Get file information from cloud storage
        /// </summary>
        /// <param name="cloudFileId">Cloud file identifier</param>
        /// <returns>File information</returns>
        Task<CloudFileInfo> GetFileInfoAsync(string cloudFileId);

        /// <summary>
        /// Download a publicly accessible file using its public URL (no authentication required)
        /// </summary>
        /// <param name="publicUrl">Public URL to the file</param>
        /// <param name="localFilePath">Local path where file should be saved</param>
        Task DownloadPublicFileAsync(string publicUrl, string localFilePath);

        /// <summary>
        /// Download a publicly accessible file to a stream using its public URL (no authentication required)
        /// </summary>
        /// <param name="publicUrl">Public URL to the file</param>
        /// <returns>Stream containing the file data</returns>
        Task<Stream> DownloadPublicFileStreamAsync(string publicUrl);

        /// <summary>
        /// Check if the provider is initialized and authenticated
        /// </summary>
        bool IsInitialized { get; }
    }

    /// <summary>
    /// Information about a cloud-stored file
    /// </summary>
    public class CloudFileInfo
    {
        public string FileId { get; set; }
        public string Filename { get; set; }
        public string Path { get; set; }
        public long FileSize { get; set; }
        public string MimeType { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string Metadata { get; set; }
    }
}
