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
    /// Google Drive cloud storage provider implementation
    /// </summary>
    public class GoogleDriveProvider : ICloudProvider
    {
        private string _accessToken;
        private string _baseFolderId;
        private readonly System.Net.Http.HttpClient _httpClient;
        private const string ApiBaseUrl = "https://www.googleapis.com/drive/v3";
        private const string UploadBaseUrl = "https://www.googleapis.com/upload/drive/v3";

        public bool IsInitialized { get; private set; }

        public GoogleDriveProvider()
        {
            _httpClient = new System.Net.Http.HttpClient();
        }

        public Task InitializeAsync(string baseFolderUrl, string credentials)
        {
            try
            {
                // Parse credentials JSON to get access token
                var credJson = JsonSerializer.Deserialize<GoogleDriveCredentials>(credentials);
                _accessToken = credJson?.AccessToken;

                if (string.IsNullOrEmpty(_accessToken))
                {
                    throw new Exception("Google Drive access token is required");
                }

                // Extract folder ID from URL or use directly
                _baseFolderId = ExtractFolderId(baseFolderUrl);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", _accessToken);

                IsInitialized = true;
                GD.Print($"Google Drive provider initialized with base folder ID: {_baseFolderId}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to initialize Google Drive provider: {ex.Message}");
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
                GD.PrintErr($"Google Drive upload failed: {ex.Message}");
                throw;
            }
        }

        public async Task<CloudFileInfo> UploadStreamAsync(Stream stream, string filename, string destinationPath)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                // First, create file metadata
                var metadata = new
                {
                    name = filename,
                    parents = new[] { _baseFolderId }
                };

                var metadataJson = JsonSerializer.Serialize(metadata);

                // Use multipart upload
                var boundary = "-------314159265358979323846";
                var delimiter = $"\r\n--{boundary}\r\n";
                var closeDelimiter = $"\r\n--{boundary}--";

                var multipartContent = new StringBuilder();
                multipartContent.Append(delimiter);
                multipartContent.Append("Content-Type: application/json; charset=UTF-8\r\n\r\n");
                multipartContent.Append(metadataJson);
                multipartContent.Append(delimiter);
                multipartContent.Append("Content-Type: application/octet-stream\r\n\r\n");

                var metadataBytes = Encoding.UTF8.GetBytes(multipartContent.ToString());
                var closingBytes = Encoding.UTF8.GetBytes(closeDelimiter);

                var url = $"{UploadBaseUrl}/files?uploadType=multipart";
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                
                var content = new MultipartContent("related", boundary);
                content.Headers.ContentType.Parameters.Clear();
                content.Headers.ContentType = new MediaTypeHeaderValue("multipart/related");
                content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));

                var jsonContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
                var streamContent = new StreamContent(stream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                content.Add(jsonContent);
                content.Add(streamContent);

                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var fileMetadata = JsonSerializer.Deserialize<GoogleDriveFileMetadata>(responseContent);

                return new CloudFileInfo
                {
                    FileId = fileMetadata.id,
                    Filename = fileMetadata.name,
                    Path = destinationPath,
                    FileSize = long.Parse(fileMetadata.size ?? "0"),
                    CreatedDate = DateTime.Parse(fileMetadata.createdTime),
                    ModifiedDate = DateTime.Parse(fileMetadata.modifiedTime),
                    MimeType = fileMetadata.mimeType,
                    Metadata = responseContent
                };
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Google Drive stream upload failed: {ex.Message}");
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
                GD.PrintErr($"Google Drive download failed: {ex.Message}");
                throw;
            }
        }

        public async Task<Stream> DownloadStreamAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{ApiBaseUrl}/files/{cloudFileId}?alt=media";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var memoryStream = new MemoryStream();
                await response.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Google Drive stream download failed: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteFileAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{ApiBaseUrl}/files/{cloudFileId}";
                var response = await _httpClient.DeleteAsync(url);
                response.EnsureSuccessStatusCode();

                GD.Print($"Deleted file from Google Drive: {cloudFileId}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Google Drive delete failed: {ex.Message}");
                throw;
            }
        }

        public async Task<CloudFileInfo> GetFileInfoAsync(string cloudFileId)
        {
            if (!IsInitialized) throw new InvalidOperationException("Provider not initialized");

            try
            {
                var url = $"{ApiBaseUrl}/files/{cloudFileId}?fields=id,name,size,mimeType,createdTime,modifiedTime";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var metadata = JsonSerializer.Deserialize<GoogleDriveFileMetadata>(responseContent);

                return new CloudFileInfo
                {
                    FileId = metadata.id,
                    Filename = metadata.name,
                    FileSize = long.Parse(metadata.size ?? "0"),
                    MimeType = metadata.mimeType,
                    CreatedDate = DateTime.Parse(metadata.createdTime),
                    ModifiedDate = DateTime.Parse(metadata.modifiedTime),
                    Metadata = responseContent
                };
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Google Drive get file info failed: {ex.Message}");
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
                GD.Print($"Downloaded public file from Google Drive to: {localFilePath}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Google Drive public file download failed: {ex.Message}");
                throw;
            }
        }

        public async Task<Stream> DownloadPublicFileStreamAsync(string publicUrl)
        {
            try
            {
                // Convert Google Drive share URL to direct download URL
                var downloadUrl = ConvertToDirectDownloadUrl(publicUrl);

                using var client = new System.Net.Http.HttpClient();
                var response = await client.GetAsync(downloadUrl);

                // Handle redirect for large files (Google Drive virus scan warning)
                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.Found)
                {
                    var redirectUrl = response.Headers.Location?.ToString();
                    if (!string.IsNullOrEmpty(redirectUrl))
                    {
                        response = await client.GetAsync(redirectUrl);
                    }
                }

                response.EnsureSuccessStatusCode();

                var memoryStream = new MemoryStream();
                await response.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Google Drive public stream download failed: {ex.Message}");
                throw;
            }
        }

        private string ConvertToDirectDownloadUrl(string url)
        {
            // Extract file ID from various Google Drive URL formats
            string fileId = ExtractFileIdFromUrl(url);

            if (!string.IsNullOrEmpty(fileId))
            {
                // Use direct download URL format
                return $"https://drive.google.com/uc?export=download&id={fileId}";
            }

            return url;
        }

        private string ExtractFileIdFromUrl(string url)
        {
            // Handle formats like:
            // https://drive.google.com/file/d/{fileId}/view
            // https://drive.google.com/open?id={fileId}
            // https://drive.google.com/uc?id={fileId}

            if (url.Contains("/file/d/"))
            {
                var parts = url.Split(new[] { "/file/d/" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    return parts[1].Split('/')[0].Split('?')[0];
                }
            }
            else if (url.Contains("id="))
            {
                var parts = url.Split(new[] { "id=" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    return parts[1].Split('&')[0];
                }
            }

            return null;
        }

        private string ExtractFolderId(string folderUrl)
        {
            if (string.IsNullOrEmpty(folderUrl)) return "root";

            // If it's already just an ID, return it
            if (!folderUrl.Contains("/") && !folderUrl.Contains("drive.google.com"))
            {
                return folderUrl;
            }

            // Extract from URL like https://drive.google.com/drive/folders/{folderId}
            var parts = folderUrl.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "folders" && i + 1 < parts.Length)
                {
                    return parts[i + 1].Split('?')[0]; // Remove query parameters if any
                }
            }

            return folderUrl;
        }

        private class GoogleDriveCredentials
        {
            public string AccessToken { get; set; }
        }

        private class GoogleDriveFileMetadata
        {
            public string id { get; set; }
            public string name { get; set; }
            public string size { get; set; }
            public string mimeType { get; set; }
            public string createdTime { get; set; }
            public string modifiedTime { get; set; }
        }
    }
}
