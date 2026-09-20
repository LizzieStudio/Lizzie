using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// OneDrive/Office 365 cloud storage provider implementation
    /// </summary>
    public class OneDriveProvider : ICloudProvider
    {
        private string _accessToken;
        private string _baseFolderPath;
        private readonly System.Net.Http.HttpClient _httpClient;

        public bool IsInitialized { get; private set; }

        public OneDriveProvider()
        {
            _httpClient = new System.Net.Http.HttpClient();
        }

        public Task InitializeAsync(string baseFolderUrl, string credentials)
        {
            try
            {
                // Parse credentials JSON to get access token
                var credJson = JsonSerializer.Deserialize<OneDriveCredentials>(credentials);
                _accessToken = credJson?.AccessToken;

                if (string.IsNullOrEmpty(_accessToken))
                {
                    throw new Exception("OneDrive access token is required");
                }

                _baseFolderPath = NormalizePath(baseFolderUrl);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    _accessToken
                );

                IsInitialized = true;
                GD.Print($"OneDrive provider initialized with base folder: {_baseFolderPath}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to initialize OneDrive provider: {ex.Message}");
                IsInitialized = false;
                throw;
            }
        }

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

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            return path.Replace("\\", "/").Trim('/');
        }

        private class OneDriveCredentials
        {
            public string AccessToken { get; set; }
        }
    }
}
