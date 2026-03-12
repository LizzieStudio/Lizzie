using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

/// <summary>
/// Example usage of the CloudAssetService
/// </summary>
public partial class AssetManagerExample : Node
{
    private CloudAssetService _assetService;

    public override void _Ready()
    {
        // Example: Initialize with Dropbox
        InitializeDropboxExample();

        // Example: Initialize with Google Drive
        // InitializeGoogleDriveExample();

        // Example: Initialize with OneDrive
        // InitializeOneDriveExample();
    }

    private async void InitializeDropboxExample()
    {
        try
        {
            _assetService = new CloudAssetService();

            // Credentials should be securely stored/retrieved
            var credentials = "{\"AccessToken\":\"your-dropbox-access-token\"}";
            
            await _assetService.InitializeAsync(
                CloudProviderType.Dropbox,
                "/MyGameProject/Assets",
                credentials
            );

            GD.Print("Asset service initialized successfully");

            // Load existing assets from current project
            if (ProjectService.Instance?.CurrentProject != null)
            {
                _assetService.LoadAssets(ProjectService.Instance.CurrentProject.Assets);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to initialize asset service: {ex.Message}");
        }
    }

    private async void InitializeGoogleDriveExample()
    {
        try
        {
            _assetService = new CloudAssetService();

            var credentials = "{\"AccessToken\":\"your-google-drive-token\"}";
            
            await _assetService.InitializeAsync(
                CloudProviderType.GoogleDrive,
                "1A2B3C4D5E6F7G8H9I0J1K2L3M4N5O",  // Google Drive folder ID
                credentials
            );

            if (ProjectService.Instance?.CurrentProject != null)
            {
                _assetService.LoadAssets(ProjectService.Instance.CurrentProject.Assets);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to initialize asset service: {ex.Message}");
        }
    }

    private async void InitializeOneDriveExample()
    {
        try
        {
            _assetService = new CloudAssetService();

            var credentials = "{\"AccessToken\":\"your-onedrive-token\"}";
            
            await _assetService.InitializeAsync(
                CloudProviderType.OneDrive,
                "Documents/MyGameProject/Assets",
                credentials
            );

            if (ProjectService.Instance?.CurrentProject != null)
            {
                _assetService.LoadAssets(ProjectService.Instance.CurrentProject.Assets);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to initialize asset service: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Upload a file
    /// </summary>
    public async Task<Asset> UploadFileExample(string localPath, string assetName)
    {
        try
        {
            var asset = await _assetService.UploadAssetAsync(localPath, assetName);
            
            // Save to project
            if (ProjectService.Instance?.CurrentProject != null)
            {
                ProjectService.Instance.CurrentProject.Assets = _assetService.GetAssetList();
            }

            GD.Print($"Uploaded: {asset.Name} - ID: {asset.AssetId}");
            return asset;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Upload failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Example: Download a file
    /// </summary>
    public async Task DownloadFileExample(Guid assetId, string downloadPath)
    {
        try
        {
            await _assetService.DownloadAssetAsync(assetId, downloadPath);
            GD.Print($"Downloaded to: {downloadPath}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Delete a file
    /// </summary>
    public async Task DeleteFileExample(Guid assetId)
    {
        try
        {
            await _assetService.DeleteAssetAsync(assetId);
            
            // Update project
            if (ProjectService.Instance?.CurrentProject != null)
            {
                ProjectService.Instance.CurrentProject.Assets = _assetService.GetAssetList();
            }

            GD.Print("Asset deleted successfully");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Delete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: List all assets
    /// </summary>
    public void ListAssetsExample()
    {
        var assets = _assetService.GetAllAssets();
        
        GD.Print("=== All Assets ===");
        foreach (var asset in assets)
        {
            GD.Print($"- {asset.Name} ({asset.OriginalFilename}) - {FormatFileSize(asset.FileSize)}");
            GD.Print($"  ID: {asset.AssetId}");
            GD.Print($"  Provider: {asset.ProviderType}");
            GD.Print($"  Uploaded: {asset.UploadedDate}");
        }
    }

    /// <summary>
    /// Example: Search assets
    /// </summary>
    public void SearchAssetsExample(string searchTerm)
    {
        var results = _assetService.SearchAssetsByName(searchTerm);

        GD.Print($"=== Search Results for '{searchTerm}' ===");
        foreach (var asset in results)
        {
            GD.Print($"- {asset.Name} ({asset.OriginalFilename})");
        }
    }

    /// <summary>
    /// Example: Download a public file without authentication
    /// </summary>
    public async Task DownloadPublicFileExample(string publicUrl, string savePath)
    {
        try
        {
            await _assetService.DownloadPublicFileAsync(publicUrl, savePath);
            GD.Print($"Public file downloaded to: {savePath}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Public download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Download a public file to stream
    /// </summary>
    public async Task<Stream> DownloadPublicFileStreamExample(string publicUrl)
    {
        try
        {
            var stream = await _assetService.DownloadPublicFileStreamAsync(publicUrl);
            GD.Print("Public file downloaded to stream");
            return stream;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Public stream download failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Example: Import a public file as an asset
    /// </summary>
    public async Task<Asset> ImportPublicFileExample(string publicUrl, string assetName)
    {
        try
        {
            var asset = await _assetService.ImportPublicFileAsAssetAsync(publicUrl, assetName);

            // Save to project
            if (ProjectService.Instance?.CurrentProject != null)
            {
                ProjectService.Instance.CurrentProject.Assets = _assetService.GetAssetList();
            }

            GD.Print($"Public file imported: {asset.Name} - ID: {asset.AssetId}");
            return asset;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Public import failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Example: Download from various public cloud URLs
    /// </summary>
    public async Task DownloadFromVariousProvidersExample()
    {
        // Dropbox public link
        await DownloadPublicFileExample(
            "https://www.dropbox.com/s/abc123/image.png?dl=0",
            "C:/Downloads/dropbox_image.png"
        );

        // Google Drive public link
        await DownloadPublicFileExample(
            "https://drive.google.com/file/d/1A2B3C4D5E6F7G/view",
            "C:/Downloads/gdrive_file.pdf"
        );

        // OneDrive public link
        await DownloadPublicFileExample(
            "https://1drv.ms/i/s!ABC123XYZ",
            "C:/Downloads/onedrive_image.jpg"
        );
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
