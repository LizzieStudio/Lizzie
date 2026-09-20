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
