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
        private const string ApiBaseUrl = "https://api.dropboxapi.com/2";
        private const string ContentBaseUrl = "https://content.dropboxapi.com/2";

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

        public async Task<CloudFileInfo> UploadFileAsync(string localFilePath, string destinationPath)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                using var stream = File.OpenRead(localFilePath);
                var filename = Path.GetFileName(localFilePath);
                return await UploadStreamAsync(stream, filename, destinationPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox upload failed: {ex.Message}");
                throw;
            }
        }

        public async Task<CloudFileInfo> UploadStreamAsync(Stream stream, string filename, string destinationPath)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var fullPath = CombinePath(_baseFolderPath, destinationPath, filename);
                var url = $"{ContentBaseUrl}/files/upload";

                var uploadArg = new
                {
                    path = fullPath,
                    mode = "add",
                    autorename = true,
                    mute = false
                };

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(uploadArg));
                request.Headers.Add("Content-Type", "application/octet-stream");
                request.Content = new StreamContent(stream);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var metadata = JsonSerializer.Deserialize<DropboxFileMetadata>(responseContent);

                return new CloudFileInfo
                {
                    FileId = metadata.id,
                    Filename = metadata.name,
                    Path = metadata.path_display,
                    FileSize = metadata.size,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.Parse(metadata.server_modified),
                    Metadata = responseContent
                };
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox stream upload failed: {ex.Message}");
                throw;
            }
        }

        public async Task DownloadFileAsync(string cloudFileId, string localFilePath)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                using var stream = await DownloadStreamAsync(cloudFileId);
                using var fileStream = File.Create(localFilePath);
                await stream.CopyToAsync(fileStream);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox download failed: {ex.Message}");
                throw;
            }
        }

        public async Task<Stream> DownloadStreamAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{ContentBaseUrl}/files/download";
                var downloadArg = new { path = cloudFileId };

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(downloadArg));

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var memoryStream = new MemoryStream();
                await response.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox stream download failed: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteFileAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{ApiBaseUrl}/files/delete_v2";
                var deleteArg = new { path = cloudFileId };

                var content = new StringContent(
                    JsonSerializer.Serialize(deleteArg),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                GD.Print($"Deleted file from Dropbox: {cloudFileId}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox delete failed: {ex.Message}");
                throw;
            }
        }

        public async Task<CloudFileInfo> GetFileInfoAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{ApiBaseUrl}/files/get_metadata";
                var metadataArg = new { path = cloudFileId };

                var content = new StringContent(
                    JsonSerializer.Serialize(metadataArg),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var metadata = JsonSerializer.Deserialize<DropboxFileMetadata>(responseContent);

                return new CloudFileInfo
                {
                    FileId = metadata.id,
                    Filename = metadata.name,
                    Path = metadata.path_display,
                    FileSize = metadata.size,
                    ModifiedDate = DateTime.Parse(metadata.server_modified),
                    Metadata = responseContent
                };
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox get file info failed: {ex.Message}");
                throw;
            }
        }

        public async Task DownloadPublicFileAsync(string publicUrl, string localFilePath)
        {
            try
            {
                using var stream = await DownloadPublicFileStreamAsync(publicUrl);
                using var fileStream = File.Create(localFilePath);
                await stream.CopyToAsync(fileStream);
                GD.Print($"Downloaded public file from Dropbox to: {localFilePath}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Dropbox public file download failed: {ex.Message}");
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
            if (string.IsNullOrEmpty(path)) return "";
            path = path.Replace("\\", "/");
            if (!path.StartsWith("/")) path = "/" + path;
            return path.TrimEnd('/');
        }

        private string CombinePath(params string[] parts)
        {
            var combined = string.Join("/", parts).Replace("\\", "/");
            while (combined.Contains("//")) combined = combined.Replace("//", "/");
            if (!combined.StartsWith("/")) combined = "/" + combined;
            return combined;
        }

        private class DropboxCredentials
        {
            public string AccessToken { get; set; }
        }

        private class DropboxFileMetadata
        {
            public string id { get; set; }
            public string name { get; set; }
            public string path_display { get; set; }
            public long size { get; set; }
            public string server_modified { get; set; }
        }
    }
}
