# Unity Developer Guide: Avatar Collection Upload Integration

## Overview

We've added a new Catalog (Avatar Collections) system to the API. Your Unity plugin needs to upload avatar bundles, cosmetics, and metadata using a two-step upload flow similar to how world maps work.

## API Endpoints

### Base URL
Use the same API base URL as other endpoints (e.g., `https://api.virtualvenues.com` or your environment's URL).

### Authentication
All endpoints require Bearer token authentication:
```
Authorization: Bearer <access_token>
```

---

## Upload Flow

### Step 1: Request Upload URLs

**Endpoint:** `POST /users/me/catalogs/upload-urls`

**Request Body:**
```json
{
  "catalogId": "optional-existing-catalog-id",
  "versionId": "optional-custom-version-id", 
  "bundleNames": [
    "avatar_base.bundle",
    "cosmetics_hats.bundle",
    "cosmetics_faces.bundle"
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `catalogId` | No | Omit to create a new catalog, or provide to add a new version to existing catalog |
| `versionId` | No | Omit to auto-generate, or provide your own (must be unique) |
| `bundleNames` | **Yes** | Array of `.bundle` filenames (no paths, no `..`, must end in `.bundle`) |

**Response:**
```json
{
  "catalogId": "abc123",
  "versionId": "def456",
  "uploadUrlBin": "https://s3.../presigned-url-for-content_catalog.bin",
  "uploadUrlHash": "https://s3.../presigned-url-for-content_catalog.hash",
  "uploadUrlBundles": [
    { "name": "avatar_base.bundle", "presignedUrl": "https://s3.../..." },
    { "name": "cosmetics_hats.bundle", "presignedUrl": "https://s3.../..." },
    { "name": "cosmetics_faces.bundle", "presignedUrl": "https://s3.../..." }
  ]
}
```

### Step 2: Upload Files to S3

Use HTTP `PUT` requests to upload each file to its presigned URL:

1. Upload `content_catalog.bin` → `uploadUrlBin`
2. Upload `content_catalog.hash` → `uploadUrlHash`
3. Upload each bundle file → corresponding `presignedUrl` in `uploadUrlBundles`

**Important:** Use `PUT` method, set `Content-Type: application/octet-stream`

### Step 3: Confirm Upload

**Endpoint:** `POST /users/me/catalogs/confirm-upload`

**Request Body:**
```json
{
  "catalogId": "abc123",
  "versionId": "def456",
  "bundleNames": [
    "avatar_base.bundle",
    "cosmetics_hats.bundle",
    "cosmetics_faces.bundle"
  ],
  "name": "My Avatar Collection",
  "versionTag": "2.2.0",
  "metadata": {
    "bundleVersion": "1.0",
    "unityVersion": "2022.3.10f1",
    "buildTarget": "WebGL",
    "avatars": [
      {
        "id": "avatar-001",
        "name": "Robot",
        "gameId": "robot_avatar",
        "guid": "abc123-def456"
      },
      {
        "id": "avatar-002", 
        "name": "Human",
        "gameId": "human_avatar",
        "guid": "xyz789-uvw012"
      }
    ],
    "cosmetics": [
      {
        "id": "cosmetic-001",
        "categoryId": "hats",
        "name": "Top Hat",
        "gameId": "top_hat",
        "guid": "hat-guid-001"
      },
      {
        "id": "cosmetic-002",
        "categoryId": "faces",
        "name": "Sunglasses",
        "gameId": "sunglasses",
        "guid": "face-guid-001"
      }
    ]
  }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `catalogId` | **Yes** | From Step 1 response |
| `versionId` | **Yes** | From Step 1 response |
| `bundleNames` | **Yes** | Same array as Step 1 |
| `name` | No | Human-readable name (required for new catalogs) |
| `versionTag` | No | Semantic version like "2.2.0" (must be unique per catalog) |
| `metadata.bundleVersion` | **Yes** | Bundle version string |
| `metadata.unityVersion` | **Yes** | Unity editor version |
| `metadata.buildTarget` | **Yes** | e.g., "WebGL", "Windows", "macOS" |
| `metadata.avatars` | No | Array of avatar definitions |
| `metadata.cosmetics` | No | Array of cosmetic definitions |

**Response:** `204 No Content` on success

---

## S3 File Structure

Files are stored at:
```
users/<userId>/catalogs/<catalogId>/versions/<versionId>/
  content_catalog.bin
  content_catalog.hash
  <bundle>.bundle
  <bundle>.bundle
  ...
```

The `contentBaseUrl` returned to clients points to this version folder.

---

## Error Responses

| Status | Meaning |
|--------|---------|
| 400 | Invalid request (bad bundle names, missing fields) |
| 401 | Unauthorized (bad/missing token) |
| 409 | Version ID or version tag already exists |
| 500 | Server error |

---

## Example Unity C# Pseudocode

```csharp
public async Task UploadCatalog(string catalogName, string versionTag, List<string> bundleFiles)
{
    // 1. Request upload URLs
    var bundleNames = bundleFiles.Select(Path.GetFileName).ToList();
    
    var uploadUrlsRequest = new {
        bundleNames = bundleNames
    };
    
    var urlsResponse = await PostAsync("/users/me/catalogs/upload-urls", uploadUrlsRequest);
    
    // 2. Upload files to S3
    await UploadFileAsync(urlsResponse.uploadUrlBin, "content_catalog.bin");
    await UploadFileAsync(urlsResponse.uploadUrlHash, "content_catalog.hash");
    
    foreach (var bundle in urlsResponse.uploadUrlBundles)
    {
        var localPath = bundleFiles.First(f => Path.GetFileName(f) == bundle.name);
        await UploadFileAsync(bundle.presignedUrl, localPath);
    }
    
    // 3. Confirm upload with metadata
    var confirmRequest = new {
        catalogId = urlsResponse.catalogId,
        versionId = urlsResponse.versionId,
        bundleNames = bundleNames,
        name = catalogName,
        versionTag = versionTag,
        metadata = new {
            bundleVersion = "1.0",
            unityVersion = Application.unityVersion,
            buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            avatars = GetAvatarMetadata(),
            cosmetics = GetCosmeticMetadata()
        }
    };
    
    await PostAsync("/users/me/catalogs/confirm-upload", confirmRequest);
}
```

---

## Prompt for Unity Developer

> **Task:** Implement Avatar Collection upload in the Unity SDK
>
> We have a new Catalog API for uploading avatar bundles and cosmetics. The flow is:
>
> 1. Call `POST /users/me/catalogs/upload-urls` with `bundleNames[]` to get presigned S3 URLs
> 2. Upload `content_catalog.bin`, `content_catalog.hash`, and all `.bundle` files to S3 using PUT
> 3. Call `POST /users/me/catalogs/confirm-upload` with catalog metadata (avatars, cosmetics, build info)
>
> The Unity plugin should:
> - Build the addressable bundles and catalog files as usual
> - Collect avatar/cosmetic metadata (id, name, gameId, guid) from the project
> - Call the upload flow endpoints
> - Show progress UI during upload
> - Handle errors (409 = version already exists, 400 = bad request)
> - Allow user to specify catalog name and optional version tag
> - Support creating new catalogs or adding versions to existing ones
>
> See the attached API documentation for request/response schemas.