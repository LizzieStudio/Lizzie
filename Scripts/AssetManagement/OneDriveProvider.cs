using System;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// OneDrive/Office 365 cloud storage provider implementation
    /// </summary>
    public class OneDriveProvider : ICloudProvider
    {
        public async Task<Stream> DownloadPublicFileStreamAsync(string publicUrl)
        {
            try
            {
                // Convert OneDrive share URL to direct download URL
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
                GD.PrintErr($"OneDrive public stream download failed: {ex.Message}");
                throw;
            }
        }

        private string ConvertToDirectDownloadUrl(string url)
        {
            // OneDrive share links can be converted to direct download
            // Replace "view.aspx" with "download.aspx" for 1drv.ms links
            if (url.Contains("1drv.ms") || url.Contains("onedrive.live.com"))
            {
                if (url.Contains("embed"))
                {
                    return url.Replace("embed", "download");
                }
                else if (url.Contains("view.aspx"))
                {
                    return url.Replace("view.aspx", "download.aspx");
                }
                else if (!url.Contains("download"))
                {
                    // Try to append download parameter
                    var separator = url.Contains("?") ? "&" : "?";
                    return url + separator + "download=1";
                }
            }

            return url;
        }
    }
}
