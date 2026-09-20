using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    _accessToken
                );

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

        public async Task<Stream> DownloadPublicFileStreamAsync(string publicUrl)
        {
            try
            {
                var fileId = ExtractFileIdFromUrl(publicUrl);
                var downloadUrl = ConvertToDirectDownloadUrl(publicUrl);

                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    UseCookies = true,
                };
                using var client = new System.Net.Http.HttpClient(handler);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                // Google sometimes returns an HTML interstitial. Parse it and re-request once.
                if (IsHtml(response))
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var confirmUrl = BuildConfirmUrl(html, fileId);
                    if (confirmUrl != null)
                    {
                        response = await client.GetAsync(confirmUrl);
                        response.EnsureSuccessStatusCode();
                    }
                }

                if (IsHtml(response))
                {
                    throw new InvalidOperationException(
                        "Google Drive returned an HTML page instead of the file. Ensure the link "
                            + "is shared with 'Anyone with the link' and points to an image file."
                    );
                }

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

        private static bool IsHtml(HttpResponseMessage response) =>
            response.Content.Headers.ContentType?.MediaType?.Contains(
                "text/html",
                StringComparison.OrdinalIgnoreCase
            ) == true;

        /// <summary>
        /// Builds the confirmed-download URL from the Google Drive interstitial HTML, which
        /// carries the file id, export, confirm and uuid as a hidden download form.
        /// </summary>
        private static string BuildConfirmUrl(string html, string fileId)
        {
            if (string.IsNullOrEmpty(fileId))
                return null;

            var confirm = Regex
                .Match(html, "name=\"confirm\"\\s+value=\"([^\"]+)\"")
                .Groups[1]
                .Value;
            if (string.IsNullOrEmpty(confirm))
                confirm = "t";
            var uuid = Regex.Match(html, "name=\"uuid\"\\s+value=\"([^\"]+)\"").Groups[1].Value;

            var url =
                $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm={confirm}";
            if (!string.IsNullOrEmpty(uuid))
                url += $"&uuid={uuid}";
            return url;
        }

        private string ConvertToDirectDownloadUrl(string url)
        {
            // Extract file ID from various Google Drive URL formats
            string fileId = ExtractFileIdFromUrl(url);

            if (!string.IsNullOrEmpty(fileId))
            {
                // The usercontent endpoint serves the bytes directly; confirm=t skips the
                // "can't scan this file" interstitial for most public files.
                return $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm=t";
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
            if (string.IsNullOrEmpty(folderUrl))
                return "root";

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
    }
}
