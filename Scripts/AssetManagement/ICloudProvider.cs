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
}
