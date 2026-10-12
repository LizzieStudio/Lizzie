using System;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Godot;

namespace Lizzie.AssetManagement
{
    /// <summary>
    /// Downloads images from links, including Google Drive and Dropbox share links.
    /// </summary>
    public static class ImageDownload
    {
        private static readonly System.Net.Http.HttpClient Client = new(
            new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            }
        )
        {
            DefaultRequestHeaders =
            {
                UserAgent = { new ProductInfoHeaderValue("Mozilla", "5.0") },
            },
        };

        public static async Task<Image> DownloadAsync(string url)
        {
            try
            {
                var bytes = await Client.GetByteArrayAsync(DirectLink(url)).ConfigureAwait(false);
                var image = new Image();
                var err = Decode(image, bytes);
                if (err == Error.Ok)
                    return image;
                GD.PrintErr(
                    $"Couldn't decode the image at {url}: {GetImageError(bytes)} "
                        + $"(Godot decode error {(int)err} {err})"
                );
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Couldn't download {url}: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Converts a "share" link into a direct link for Google Drive or Dropbox,
        /// otherwise returns the link.
        /// </summary>
        public static string DirectLink(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return url;
            var query = HttpUtility.ParseQueryString(uri.Query);
            switch (uri.Host)
            {
                case "drive.google.com":
                case "docs.google.com":
                    // Share links look like /file/d/{id}/view, /file/u/0/d/{id}/view,
                    // /open?id={id} or /uc?id={id}.
                    var match = Regex.Match(uri.AbsolutePath, @"^/file/(?:u/\d+/)?d/([^/]+)");
                    var id = match.Success ? match.Groups[1].Value : query["id"];
                    // confirm=t skips the "can't scan this file" page for most files.
                    return id == null
                        ? url
                        : $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t";
                case "www.dropbox.com":
                case "dropbox.com":
                    query["dl"] = "1";
                    return new UriBuilder(uri) { Query = query.ToString() }
                        .Uri
                        .AbsoluteUri;
                default:
                    return url;
            }
        }

        /// <summary>
        /// Detects the file type and decodes it using the proper loader.
        /// </summary>
        private static Error Decode(Image image, byte[] b) =>
            b switch
            {
                [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => image.LoadPngFromBuffer(b),
                [0xFF, 0xD8, ..] => image.LoadJpgFromBuffer(b),
                [
                    (byte)'R',
                    (byte)'I',
                    (byte)'F',
                    (byte)'F',
                    _,
                    _,
                    _,
                    _,
                    (byte)'W',
                    (byte)'E',
                    (byte)'B',
                    (byte)'P',
                    ..,
                ] => image.LoadWebpFromBuffer(b),
                [(byte)'B', (byte)'M', ..] => image.LoadBmpFromBuffer(b),
                _ => Error.FileUnrecognized,
            };

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
