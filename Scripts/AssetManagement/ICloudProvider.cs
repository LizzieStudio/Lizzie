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
        /// Download a publicly accessible file to a stream using its public URL (no authentication required)
        /// </summary>
        /// <param name="publicUrl">Public URL to the file</param>
        /// <returns>Stream containing the file data</returns>
        Task<Stream> DownloadPublicFileStreamAsync(string publicUrl);
    }
}
