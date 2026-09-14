using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Service for managing cloud-stored assets
    /// </summary>
    public class CloudAssetService
    {
        private ICloudProvider _provider;
        private CloudProviderType _providerType;
        private readonly Dictionary<SnowTag, Asset> _assets;

        public CloudAssetService()
        {
            _assets = new Dictionary<SnowTag, Asset>();
        }

        /// <summary>
        /// Initialize the service with a specific cloud provider
        /// </summary>
        /// <param name="providerType">Type of cloud provider</param>
        /// <param name="baseFolderUrl">Base folder URL in the cloud</param>
        /// <param name="credentials">Provider-specific credentials (JSON format)</param>
        public async Task InitializeAsync(
            CloudProviderType providerType,
            string baseFolderUrl,
            string credentials
        )
        {
            _providerType = providerType;

            _provider = providerType switch
            {
                CloudProviderType.Dropbox => new DropboxProvider(),
                CloudProviderType.GoogleDrive => new GoogleDriveProvider(),
                CloudProviderType.OneDrive => new OneDriveProvider(),
                _ => throw new ArgumentException($"Unsupported provider type: {providerType}"),
            };

            if (!string.IsNullOrEmpty(baseFolderUrl) && !string.IsNullOrEmpty(credentials))
            {
                await _provider.InitializeAsync(baseFolderUrl, credentials);
                GD.Print($"CloudAssetService initialized with {providerType} provider");
            }
        }

        /// <summary>
        /// Upload a file and create an asset record
        /// </summary>
        /// <param name="localFilePath">Path to the local file</param>
        /// <param name="assetName">User-defined name for the asset</param>
        /// <param name="destinationPath">Optional destination path in cloud (subfolder)</param>
        /// <returns>The created asset</returns>
        public async Task<Asset> UploadAssetAsync(
            string localFilePath,
            string assetName,
            string destinationPath = ""
        )
        {
            if (_provider == null || !_provider.IsInitialized)
            {
                throw new InvalidOperationException("Service not initialized");
            }

            if (!File.Exists(localFilePath))
            {
                throw new FileNotFoundException($"File not found: {localFilePath}");
            }

            try
            {
                var fileInfo = new FileInfo(localFilePath);
                var cloudFileInfo = await _provider.UploadFileAsync(localFilePath, destinationPath);

                var asset = new Asset
                {
                    Id = Snowport.Clock.CreateTag(),
                    Name = assetName,
                    OriginalFilename = Path.GetFileName(localFilePath),
                    ProviderType = _providerType,
                    CloudFileId = cloudFileInfo.FileId,
                    CloudPath = cloudFileInfo.Path,
                    FileSize = cloudFileInfo.FileSize,
                    MimeType = cloudFileInfo.MimeType ?? GetMimeType(localFilePath),
                    UploadedDate = DateTime.UtcNow,
                    LastModifiedDate = cloudFileInfo.ModifiedDate,
                    ProviderMetadata = cloudFileInfo.Metadata,
                };

                _assets[asset.Id] = asset;
                GD.Print($"Asset uploaded: {assetName} ({asset.Id})");

                return asset;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to upload asset: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Upload a file from a stream and create an asset record
        /// </summary>
        /// <param name="stream">Stream containing the file data</param>
        /// <param name="filename">Filename for the asset</param>
        /// <param name="assetName">User-defined name for the asset</param>
        /// <param name="destinationPath">Optional destination path in cloud</param>
        /// <returns>The created asset</returns>
        public async Task<Asset> UploadAssetStreamAsync(
            Stream stream,
            string filename,
            string assetName,
            string destinationPath = ""
        )
        {
            if (_provider == null || !_provider.IsInitialized)
            {
                throw new InvalidOperationException("Service not initialized");
            }

            try
            {
                var cloudFileInfo = await _provider.UploadStreamAsync(
                    stream,
                    filename,
                    destinationPath
                );

                var asset = new Asset
                {
                    Id = Snowport.Clock.CreateTag(),
                    Name = assetName,
                    OriginalFilename = filename,
                    ProviderType = _providerType,
                    CloudFileId = cloudFileInfo.FileId,
                    CloudPath = cloudFileInfo.Path,
                    FileSize = cloudFileInfo.FileSize,
                    MimeType = cloudFileInfo.MimeType ?? GetMimeType(filename),
                    UploadedDate = DateTime.UtcNow,
                    LastModifiedDate = cloudFileInfo.ModifiedDate,
                    ProviderMetadata = cloudFileInfo.Metadata,
                };

                _assets[asset.Id] = asset;
                GD.Print($"Asset uploaded from stream: {assetName} ({asset.Id})");

                return asset;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to upload asset from stream: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Download an asset to a local file
        /// </summary>
        /// <param name="assetId">ID of the asset to download</param>
        /// <param name="localFilePath">Local path where file should be saved</param>
        public async Task DownloadAssetAsync(SnowTag assetId, string localFilePath)
        {
            if (_provider == null || !_provider.IsInitialized)
            {
                throw new InvalidOperationException("Service not initialized");
            }

            if (!_assets.TryGetValue(assetId, out var asset))
            {
                throw new KeyNotFoundException($"Asset not found: {assetId}");
            }

            try
            {
                await _provider.DownloadFileAsync(asset.CloudFileId, localFilePath);
                GD.Print($"Asset downloaded: {asset.Name} to {localFilePath}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to download asset: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Download an asset to a stream
        /// </summary>
        /// <param name="assetId">ID of the asset to download</param>
        /// <returns>Stream containing the file data</returns>
        public async Task<Stream> DownloadAssetStreamAsync(SnowTag assetId)
        {
            if (_provider == null || !_provider.IsInitialized)
            {
                throw new InvalidOperationException("Service not initialized");
            }

            if (!_assets.TryGetValue(assetId, out var asset))
            {
                throw new KeyNotFoundException($"Asset not found: {assetId}");
            }

            try
            {
                var stream = await _provider.DownloadStreamAsync(asset.CloudFileId);
                GD.Print($"Asset downloaded to stream: {asset.Name}");
                return stream;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to download asset to stream: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete an asset from cloud storage and remove its record
        /// </summary>
        /// <param name="assetId">ID of the asset to delete</param>
        public async Task DeleteAssetAsync(SnowTag assetId)
        {
            if (_provider == null || !_provider.IsInitialized)
            {
                throw new InvalidOperationException("Service not initialized");
            }

            if (!_assets.TryGetValue(assetId, out var asset))
            {
                throw new KeyNotFoundException($"Asset not found: {assetId}");
            }

            try
            {
                await _provider.DeleteFileAsync(asset.CloudFileId);
                _assets.Remove(assetId);
                GD.Print($"Asset deleted: {asset.Name} ({assetId})");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to delete asset: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get an asset by its ID
        /// </summary>
        public Asset GetAsset(SnowTag assetId)
        {
            return _assets.TryGetValue(assetId, out var asset) ? asset : null;
        }

        /// <summary>
        /// Get all assets
        /// </summary>
        public IEnumerable<Asset> GetAllAssets()
        {
            return _assets.Values;
        }

        /// <summary>
        /// Search assets by name
        /// </summary>
        public IEnumerable<Asset> SearchAssetsByName(string searchTerm)
        {
            return _assets.Values.Where(a =>
                a.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || a.OriginalFilename.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// Add an existing asset record (used when loading from project file)
        /// </summary>
        public void AddAsset(Asset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));
            _assets[asset.Id] = asset;
        }

        /// <summary>
        /// Load assets from a list (used when loading project)
        /// </summary>
        public void LoadAssets(IEnumerable<Asset> assets)
        {
            _assets.Clear();
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    _assets[asset.Id] = asset;
                }
            }
            GD.Print($"Loaded {_assets.Count} assets");
        }

        /// <summary>
        /// Get the list of all assets (for saving to project)
        /// </summary>
        public List<Asset> GetAssetList()
        {
            return _assets.Values.ToList();
        }

        /// <summary>
        /// Download a publicly accessible file without authentication
        /// </summary>
        /// <param name="publicUrl">Public URL to the file</param>
        /// <param name="localFilePath">Local path where file should be saved</param>
        public async Task DownloadPublicFileAsync(string publicUrl, string localFilePath)
        {
            if (_provider == null)
            {
                throw new InvalidOperationException(
                    "Provider not initialized. Call InitializeAsync first."
                );
            }

            try
            {
                await _provider.DownloadPublicFileAsync(publicUrl, localFilePath);
                GD.Print($"Public file downloaded to: {localFilePath}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to download public file: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Download a publicly accessible file to a stream without authentication
        /// </summary>
        /// <param name="publicUrl">Public URL to the file</param>
        /// <returns>Stream containing the file data</returns>
        public async Task<Stream> DownloadPublicFileStreamAsync(string publicUrl)
        {
            if (_provider == null)
            {
                throw new InvalidOperationException(
                    "Provider not initialized. Call InitializeAsync first."
                );
            }

            try
            {
                var stream = await _provider.DownloadPublicFileStreamAsync(publicUrl);
                GD.Print($"Public file downloaded to stream");
                return stream;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to download public file to stream: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Download a publicly accessible file and create an asset record
        /// </summary>
        /// <param name="publicUrl">Public URL to the file</param>
        /// <param name="assetName">User-defined name for the asset</param>
        /// <param name="tempDownloadPath">Optional temporary path for download (if null, uses temp folder)</param>
        /// <returns>The created asset with local file information</returns>
        public async Task<Asset> ImportPublicFileAsAssetAsync(
            string publicUrl,
            string assetName,
            string tempDownloadPath = null
        )
        {
            if (_provider == null)
            {
                throw new InvalidOperationException(
                    "Provider not initialized. Call InitializeAsync first."
                );
            }

            try
            {
                // Use temp path if not provided
                if (string.IsNullOrEmpty(tempDownloadPath))
                {
                    var filename =
                        Path.GetFileName(publicUrl.Split('?')[0]) ?? $"asset_{Guid.NewGuid()}.tmp";
                    tempDownloadPath = Path.Combine(Path.GetTempPath(), filename);
                }

                // Download the public file
                await _provider.DownloadPublicFileAsync(publicUrl, tempDownloadPath);

                // Create asset record (file is now local, not in cloud storage)
                var fileInfo = new FileInfo(tempDownloadPath);
                var asset = new Asset
                {
                    Id = Snowport.Clock.CreateTag(),
                    Name = assetName,
                    OriginalFilename = Path.GetFileName(tempDownloadPath),
                    ProviderType = _providerType,
                    CloudFileId = publicUrl, // Store the public URL as the file ID
                    CloudPath = publicUrl,
                    FileSize = fileInfo.Length,
                    MimeType = GetMimeType(tempDownloadPath),
                    UploadedDate = DateTime.UtcNow,
                    LastModifiedDate = fileInfo.LastWriteTimeUtc,
                    ProviderMetadata =
                        $"{{\"PublicUrl\":\"{publicUrl}\",\"ImportedFrom\":\"PublicFile\"}}",
                };

                _assets[asset.Id] = asset;
                GD.Print($"Public file imported as asset: {assetName} ({asset.Id})");

                return asset;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to import public file as asset: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update an asset's metadata
        /// </summary>
        public void UpdateAsset(Asset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));
            if (!_assets.ContainsKey(asset.Id))
            {
                throw new KeyNotFoundException($"Asset not found: {asset.Id}");
            }

            _assets[asset.Id] = asset;
            asset.LastModifiedDate = DateTime.UtcNow;
        }

        /// <summary>
        /// Check if the service is initialized
        /// </summary>
        public bool IsInitialized => _provider?.IsInitialized ?? false;

        /// <summary>
        /// Get the current provider type
        /// </summary>
        public CloudProviderType? ProviderType =>
            _provider?.IsInitialized == true ? _providerType : null;

        private string GetMimeType(string filename)
        {
            var extension = Path.GetExtension(filename).ToLowerInvariant();
            return extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".zip" => "application/zip",
                _ => "application/octet-stream",
            };
        }

        /// <summary>
        /// Downloads an image from a Google Drive public URL and applies it as the texture
        /// on <see cref="_testSprite"/>. The image is decoded entirely in-memory; nothing is
        /// written to disk.
        /// </summary>
        /// <param name="url">Public URL to download from</param>
        /// <returns>Record (string, image). If success, string is empty. Otherwise it contains the error message </returns>
        public async Task<(string, Image)> DownloadImageAsync(string url)
        {
            try
            {
                var service = new CloudAssetService();
                await service.InitializeAsync(
                    CloudProviderType.GoogleDrive,
                    string.Empty,
                    string.Empty
                );

                using var stream = await service.DownloadPublicFileStreamAsync(url);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();

                var image = new Image();
                var err = image.LoadPngFromBuffer(bytes);
                if (err != Error.Ok)
                    err = image.LoadJpgFromBuffer(bytes);
                if (err != Error.Ok)
                    err = image.LoadWebpFromBuffer(bytes);
                if (err != Error.Ok)
                    err = image.LoadBmpFromBuffer(bytes);

                if (err != Error.Ok)
                {
                    var s =
                        $"DownloadImage failed to decode {url}: {GetImageError(bytes)} "
                        + $"(Godot decode error {(int)err} {err})";
                    GD.PrintErr(s);
                    return (s, new Image());
                }

                return (string.Empty, image);
            }
            catch (Exception ex)
            {
                var s = $"DownloadImage failed for {url}: {ex.Message}";
                GD.PrintErr(s);
                return (s, new Image());
            }
        }

        /// <summary>
        /// Writes information for an image error message:
        /// * if the bytes are HTML instead of an image
        /// * the detected image format,
        /// * the dimensions and color space.
        /// </summary>
        private static string GetImageError(byte[] b)
        {
            if (b == null || b.Length == 0)
                return "no data was downloaded (0 bytes)";
            if (b.Length < 16)
                return $"only {b.Length} bytes downloaded (too small to be an image)";

            int i = 0;
            while (i < b.Length && (b[i] == ' ' || b[i] == '\n' || b[i] == '\r' || b[i] == '\t'))
                i++;
            if (i < b.Length && b[i] == '<')
                return $"the server returned a web page, not an image file ({b.Length} bytes) - "
                    + "check the link is shared 'Anyone with the link' and points to an image";

            if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
                return $"PNG image ({b.Length} bytes)";
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
                return $"GIF image ({b.Length} bytes) - Godot cannot load GIF here; use PNG or JPEG";
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F')
                return $"WebP/RIFF image ({b.Length} bytes)";
            if (b[0] == 'B' && b[1] == 'M')
                return $"BMP image ({b.Length} bytes)";
            if (b[0] == 0xFF && b[1] == 0xD8)
                return GetJpegError(b);

            return $"unrecognized format ({b.Length} bytes; first bytes "
                + $"{b[0]:X2} {b[1]:X2} {b[2]:X2} {b[3]:X2})";
        }

        /// <summary>Writes information for a JPEG error message.</summary>
        private static string GetJpegError(byte[] b)
        {
            int p = 2;
            while (p + 9 < b.Length)
            {
                if (b[p] != 0xFF)
                {
                    p++;
                    continue;
                }
                byte marker = b[p + 1];
                // Markers with no length payload.
                if (
                    marker == 0xD8
                    || marker == 0xD9
                    || marker == 0x01
                    || (marker >= 0xD0 && marker <= 0xD7)
                )
                {
                    p += 2;
                    continue;
                }
                int len = (b[p + 2] << 8) | b[p + 3];
                // Start-Of-Frame markers (C0-CF) except DHT(C4), JPG(C8) and DAC(CC).
                bool isSof =
                    marker >= 0xC0
                    && marker <= 0xCF
                    && marker != 0xC4
                    && marker != 0xC8
                    && marker != 0xCC;
                if (isSof)
                {
                    int precision = b[p + 4];
                    int height = (b[p + 5] << 8) | b[p + 6];
                    int width = (b[p + 7] << 8) | b[p + 8];
                    int comps = b[p + 9];
                    string note =
                        comps == 4
                            ? " - this is a CMYK/YCCK (4-channel) JPEG, which Godot cannot decode; "
                                + "convert it to an RGB PNG or JPEG"
                            : marker == 0xC2
                                ? " - progressive JPEG, which Godot may not decode; re-save as baseline or PNG"
                                : "";
                    return $"JPEG {width}x{height}, {precision}-bit, {comps} component(s){note}";
                }
                p += 2 + len;
            }
            return $"JPEG image ({b.Length} bytes; frame header not found)";
        }
    }
}
