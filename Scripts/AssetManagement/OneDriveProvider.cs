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
        private const string GraphApiBaseUrl = "https://graph.microsoft.com/v1.0";

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
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", _accessToken);

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
                GD.PrintErr($"OneDrive upload failed: {ex.Message}");
                throw;
            }
        }

        public async Task<CloudFileInfo> UploadStreamAsync(Stream stream, string filename, string destinationPath)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var fullPath = CombinePath(_baseFolderPath, destinationPath, filename);
                
                // For small files (< 4MB), use simple upload
                if (stream.Length < 4 * 1024 * 1024)
                {
                    var url = $"{GraphApiBaseUrl}/me/drive/root:{fullPath}:/content";
                    
                    var content = new StreamContent(stream);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    var response = await _httpClient.PutAsync(url, content);
                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var metadata = JsonSerializer.Deserialize<OneDriveFileMetadata>(responseContent);

                    return new CloudFileInfo
                    {
                        FileId = metadata.id,
                        Filename = metadata.name,
                        Path = fullPath,
                        FileSize = metadata.size,
                        CreatedDate = DateTime.Parse(metadata.createdDateTime),
                        ModifiedDate = DateTime.Parse(metadata.lastModifiedDateTime),
                        MimeType = metadata.file?.mimeType,
                        Metadata = responseContent
                    };
                }
                else
                {
                    throw new NotImplementedException("Large file upload (>4MB) requires upload session API");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OneDrive stream upload failed: {ex.Message}");
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
                GD.PrintErr($"OneDrive download failed: {ex.Message}");
                throw;
            }
        }

        public async Task<Stream> DownloadStreamAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{GraphApiBaseUrl}/me/drive/items/{cloudFileId}/content";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var memoryStream = new MemoryStream();
                await response.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OneDrive stream download failed: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteFileAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{GraphApiBaseUrl}/me/drive/items/{cloudFileId}";
                var response = await _httpClient.DeleteAsync(url);
                response.EnsureSuccessStatusCode();

                GD.Print($"Deleted file from OneDrive: {cloudFileId}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OneDrive delete failed: {ex.Message}");
                throw;
            }
        }

        public async Task<CloudFileInfo> GetFileInfoAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{GraphApiBaseUrl}/me/drive/items/{cloudFileId}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var metadata = JsonSerializer.Deserialize<OneDriveFileMetadata>(responseContent);

                return new CloudFileInfo
                {
                    FileId = metadata.id,
                    Filename = metadata.name,
                    FileSize = metadata.size,
                    MimeType = metadata.file?.mimeType,
                    CreatedDate = DateTime.Parse(metadata.createdDateTime),
                    ModifiedDate = DateTime.Parse(metadata.lastModifiedDateTime),
                    Metadata = responseContent
                };
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OneDrive get file info failed: {ex.Message}");
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
                GD.Print($"Downloaded public file from OneDrive to: {localFilePath}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OneDrive public file download failed: {ex.Message}");
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

        private string ExtractFolderId(string folderUrl)
        {
            if (string.IsNullOrEmpty(folderUrl)) return "root";

            // If it's already just an ID, return it
            if (!folderUrl.Contains("/") && !folderUrl.Contains("onedrive.live.com"))
            {
                return folderUrl;
            }

            // For OneDrive URLs, might need custom extraction logic
            // For now, assume it's a direct folder ID or path
            return folderUrl;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace("\\", "/").Trim('/');
        }

        private string CombinePath(params string[] parts)
        {
            var combined = string.Join("/", parts).Replace("\\", "/");
            while (combined.Contains("//")) combined = combined.Replace("//", "/");
            return combined.Trim('/');
        }

        private class OneDriveCredentials
        {
            public string AccessToken { get; set; }
        }

        private class OneDriveFileMetadata
        {
            public string id { get; set; }
            public string name { get; set; }
            public long size { get; set; }
            public string createdDateTime { get; set; }
            public string lastModifiedDateTime { get; set; }
            public OneDriveFileInfo file { get; set; }
        }

        private class OneDriveFileInfo
        {
            public string mimeType { get; set; }
        }
    }
}
