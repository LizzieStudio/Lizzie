using System;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Dropbox cloud storage provider implementation
    /// </summary>
    public class DropboxProvider : ICloudProvider
    {
        public async Task<Stream> DownloadPublicFileStreamAsync(string publicUrl)
        {
            try
            {
                // Convert share URL to direct download URL if needed
                var downloadUrl = ConvertToDirectDownloadUrl(publicUrl);

                using var client = new System.Net.Http.HttpClient();
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                var memoryStream = new MemoryStream();
                await response.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox public stream download failed: {ex.Message}");
                throw;
            }
        }

        private string ConvertToDirectDownloadUrl(string url)
        {
            // Dropbox share links can be converted to direct download by changing dl=0 to dl=1
            // or by changing www.dropbox.com to dl.dropboxusercontent.com
            if (url.Contains("www.dropbox.com") && url.Contains("?"))
            {
                // Replace dl=0 with dl=1 for direct download
                return url.Replace("dl=0", "dl=1");
            }
            else if (url.Contains("www.dropbox.com"))
            {
                // Add dl=1 parameter
                return url + "?dl=1";
            }

            return url;
        }
    }
}
