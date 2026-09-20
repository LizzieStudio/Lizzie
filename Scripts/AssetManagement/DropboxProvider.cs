using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Dropbox cloud storage provider implementation
    /// </summary>
    public class DropboxProvider : ICloudProvider
    {
        private string _accessToken;
        private string _baseFolderPath;
        private readonly System.Net.Http.HttpClient _httpClient;

        public bool IsInitialized { get; private set; }

        public DropboxProvider()
        {
            _httpClient = new System.Net.Http.HttpClient();
        }

        public Task InitializeAsync(string baseFolderUrl, string credentials)
        {
            try
            {
                // Parse credentials JSON to get access token
                var credJson = JsonSerializer.Deserialize<DropboxCredentials>(credentials);
                _accessToken = credJson?.AccessToken;

                if (string.IsNullOrEmpty(_accessToken))
                {
                    throw new Exception("Dropbox access token is required");
                }

                _baseFolderPath = NormalizePath(baseFolderUrl);
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                IsInitialized = true;
                GD.Print($"Dropbox provider initialized with base folder: {_baseFolderPath}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to initialize Dropbox provider: {ex.Message}");
                IsInitialized = false;
                throw;
            }
        }

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

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            path = path.Replace("\\", "/");
            if (!path.StartsWith("/"))
                path = "/" + path;
            return path.TrimEnd('/');
        }

        private class DropboxCredentials
        {
            public string AccessToken { get; set; }
        }
    }
}
