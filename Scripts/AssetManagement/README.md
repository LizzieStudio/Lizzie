# Cloud Asset Management System

This system provides a unified interface for managing assets stored in cloud services including Dropbox, Google Drive, and OneDrive/Office 365.

## Features

- Upload files to cloud storage
- Download files from cloud storage
- Delete files from cloud storage
- Track asset metadata (filename, size, upload date, etc.)
- Support for multiple cloud providers
- Integration with Project system

## Architecture

### Core Classes

- **Asset**: Data model representing a cloud-stored file
- **ICloudProvider**: Interface that all cloud providers implement
- **CloudAssetService**: Main service for managing assets
- **CloudProviderType**: Enum of supported cloud providers

### Cloud Providers

- **DropboxProvider**: Dropbox API implementation
- **GoogleDriveProvider**: Google Drive API implementation
- **OneDriveProvider**: OneDrive/Office 365 Graph API implementation

## Usage

### 1. Initialize the Service

```csharp
using Lizzie.AssetManagement;

var assetService = new CloudAssetService();

// For Dropbox
var dropboxCredentials = "{\"AccessToken\":\"your-dropbox-token\"}";
await assetService.InitializeAsync(
    CloudProviderType.Dropbox, 
    "/MyProject/Assets",
    dropboxCredentials
);

// For Google Drive
var driveCredentials = "{\"AccessToken\":\"your-google-token\"}";
await assetService.InitializeAsync(
    CloudProviderType.GoogleDrive,
    "1A2B3C4D5E6F7G8H9I",  // Folder ID
    driveCredentials
);

// For OneDrive
var oneDriveCredentials = "{\"AccessToken\":\"your-onedrive-token\"}";
await assetService.InitializeAsync(
    CloudProviderType.OneDrive,
    "Documents/MyProject/Assets",
    oneDriveCredentials
);
```

### 2. Upload an Asset

```csharp
// Upload from file
var asset = await assetService.UploadAssetAsync(
    localFilePath: "C:/MyFiles/image.png",
    assetName: "Character Portrait",
    destinationPath: "Images"  // Optional subfolder
);

// Upload from stream
using var stream = File.OpenRead("C:/MyFiles/data.json");
var asset = await assetService.UploadAssetStreamAsync(
    stream: stream,
    filename: "data.json",
    assetName: "Game Data",
    destinationPath: "Data"
);
```

### 3. Download an Asset

```csharp
// Download to file
await assetService.DownloadAssetAsync(
    assetId: asset.AssetId,
    localFilePath: "C:/Downloads/image.png"
);

// Download to stream
using var stream = await assetService.DownloadAssetStreamAsync(asset.AssetId);
// Process stream...
```

### 4. Delete an Asset

```csharp
await assetService.DeleteAssetAsync(asset.AssetId);
```

### 5. Query Assets

```csharp
// Get specific asset
var asset = assetService.GetAsset(assetId);

// Get all assets
var allAssets = assetService.GetAllAssets();

// Search by name
var matchingAssets = assetService.SearchAssetsByName("portrait");
```

### 6. Download Public Files (No Authentication Required)

```csharp
// Download a public file directly to disk
await assetService.DownloadPublicFileAsync(
    publicUrl: "https://www.dropbox.com/s/abc123/image.png?dl=0",
    localFilePath: "C:/Downloads/image.png"
);

// Download a public file to stream
using var stream = await assetService.DownloadPublicFileStreamAsync(
    publicUrl: "https://drive.google.com/file/d/abc123xyz/view"
);

// Import a public file as an asset
var asset = await assetService.ImportPublicFileAsAssetAsync(
    publicUrl: "https://1drv.ms/i/s!abc123xyz",
    assetName: "Public Resource",
    tempDownloadPath: "C:/Temp/resource.png"  // Optional
);
```

**Supported Public URL Formats:**

- **Dropbox**: 
  - `https://www.dropbox.com/s/{key}/{filename}?dl=0`
  - `https://dl.dropboxusercontent.com/{path}`

- **Google Drive**:
  - `https://drive.google.com/file/d/{fileId}/view`
  - `https://drive.google.com/open?id={fileId}`
  - `https://drive.google.com/uc?id={fileId}`

- **OneDrive**:
  - `https://1drv.ms/{shortlink}`
  - `https://onedrive.live.com/embed?{params}`
  - `https://onedrive.live.com/view.aspx?{params}`

### 7. Integration with Project

```csharp
// Load assets from project
var project = ProjectService.Instance.CurrentProject;
assetService.LoadAssets(project.Assets);

// Save assets to project
project.Assets = assetService.GetAssetList();
```

## Authentication

Each cloud provider requires OAuth 2.0 authentication. You'll need to:

### Dropbox
1. Create an app at https://www.dropbox.com/developers
2. Get an access token
3. Pass it in credentials JSON: `{"AccessToken":"your-token"}`

### Google Drive
1. Create a project in Google Cloud Console
2. Enable Google Drive API
3. Get OAuth 2.0 credentials
4. Obtain access token
5. Pass it in credentials JSON: `{"AccessToken":"your-token"}`

### OneDrive/Office 365
1. Register app in Azure Portal
2. Add Microsoft Graph permissions
3. Obtain access token
4. Pass it in credentials JSON: `{"AccessToken":"your-token"}`

## Asset Properties

Each `Asset` object contains:

- **AssetId**: Unique identifier (Guid)
- **Name**: User-defined name
- **OriginalFilename**: Original file name when uploaded
- **ProviderType**: Cloud provider type (Dropbox, GoogleDrive, OneDrive)
- **CloudFileId**: Provider-specific file identifier
- **CloudPath**: Path in cloud storage
- **FileSize**: Size in bytes
- **MimeType**: MIME type of the file
- **UploadedDate**: When asset was uploaded
- **LastModifiedDate**: Last modification date
- **ProviderMetadata**: Provider-specific metadata (JSON)

## Error Handling

All async methods can throw exceptions:

```csharp
try
{
    var asset = await assetService.UploadAssetAsync(filePath, name);
}
catch (InvalidOperationException ex)
{
    // Service not initialized
}
catch (FileNotFoundException ex)
{
    // Local file not found
}
catch (HttpRequestException ex)
{
    // Network/API error
}
catch (Exception ex)
{
    // Other errors
}
```

## Thread Safety

The `CloudAssetService` is not thread-safe. If you need concurrent access, implement appropriate locking mechanisms.

## Limitations

- OneDrive: Files larger than 4MB require upload session API (not yet implemented)
- All providers: Requires valid access tokens (token refresh not implemented) for authenticated operations
- Network operations may timeout on slow connections
- Public file downloads work independently of authentication but still require provider initialization
- Public URL format parsing may not cover all edge cases

## Public File Downloads

Public file downloads are a special feature that allows downloading files shared publicly without requiring authentication. This is useful for:

- Importing reference materials from public repositories
- Downloading assets shared by team members
- Accessing community-contributed resources

**Important Notes:**
- The service must still be initialized (call `InitializeAsync`) even for public downloads
- URL format conversion is automatic but may not work for all share link variations
- Large files from Google Drive may require handling redirect responses
- Public files can be imported as assets for tracking purposes

**URL Conversion Examples:**

Dropbox:
- Input: `https://www.dropbox.com/s/xyz/file.png?dl=0`
- Converted: `https://www.dropbox.com/s/xyz/file.png?dl=1`

Google Drive:
- Input: `https://drive.google.com/file/d/ABC123/view`
- Converted: `https://drive.google.com/uc?export=download&id=ABC123`

OneDrive:
- Input: `https://onedrive.live.com/view.aspx?resid=ABC`
- Converted: `https://onedrive.live.com/download.aspx?resid=ABC`

## Future Enhancements

- Token refresh mechanism
- Large file upload support for all providers
- Progress reporting for uploads/downloads
- Batch operations
- Asset thumbnail generation
- Asset versioning
- Offline mode with sync
