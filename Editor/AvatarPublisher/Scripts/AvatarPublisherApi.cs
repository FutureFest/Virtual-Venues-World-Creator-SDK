using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Auth0;
using UnityEngine;
using UnityEngine.Networking;

namespace AvatarPublisher
{
    #region Request/Response Data Classes

    [Serializable]
    public class CatalogUploadUrlsRequest
    {
        public string catalogId;
        public string versionId;
        public string[] bundleNames;
    }

    [Serializable]
    public class BundleUploadUrl
    {
        public string name;
        public string presignedUrl;
    }

    [Serializable]
    public class CatalogUploadUrlsResponse
    {
        public string catalogId;
        public string versionId;
        public string uploadUrlBin;
        public string uploadUrlHash;
        public BundleUploadUrl[] uploadUrlBundles;
    }

    [Serializable]
    public class AvatarMetadata
    {
        public string id;
        public string name;
        public string gameId;
        public string guid;
    }

    [Serializable]
    public class CosmeticMetadata
    {
        public string id;
        public string categoryId;
        public string name;
        public string gameId;
        public string guid;
    }

    [Serializable]
    public class CatalogMetadata
    {
        public string bundleVersion;
        public string unityVersion;
        public string buildTarget;
        public string defaultAvatarId;
        public AvatarMetadata[] avatars;
        public CosmeticMetadata[] cosmetics;
    }

    [Serializable]
    public class CatalogConfirmRequest
    {
        public string catalogId;
        public string versionId;
        public string[] bundleNames;
        public string name;
        public string versionTag;
        public CatalogMetadata metadata;
    }

    [Serializable]
    public class CatalogSummary
    {
        public string catalogId;
        public string name;
        public string latestVersionId;
        public string latestVersionTag;
        public string defaultAvatarId;
        public string updatedAt; // RFC3339 UTC; backend sets it on every version upload
    }

    [Serializable]
    public class CatalogDetail
    {
        public string catalogId;
        public string name;
        public string description;
        public string versionId;
        public string versionTag;
        public string contentBaseUrl;
        public string[] bundleNames;
        public string defaultAvatarId;
        public AvatarMetadata[] avatars;
        public CosmeticMetadata[] cosmetics;
    }

    [Serializable]
    public class CatalogVersionSummary
    {
        public string versionId;
        public string versionTag;
        public string createdAt;
        public string defaultAvatarId;
    }

    [Serializable]
    public class CatalogSummaryListResponse
    {
        public CatalogSummary[] catalogs;
    }

    [Serializable]
    public class CatalogVersionListResponse
    {
        public CatalogVersionSummary[] versions;
    }

    [Serializable]
    public class CreateCatalogRequest
    {
        public string name;
    }

    [Serializable]
    public class CreateCatalogResponse
    {
        public string catalogId;
        public string name;
    }

    [Serializable]
    public class RenameCatalogRequest
    {
        public string name;
    }

    #endregion

    public class CatalogInUseException : Exception
    {
        public CatalogInUseException(string message) : base(message) { }
    }

    public class CatalogVersionExistsException : Exception
    {
        public CatalogVersionExistsException(string message) : base(message) { }
    }

    public static class AvatarPublisherApi
    {
        private const string BASE_URL = "https://jsxf2xqpn4.execute-api.us-east-1.amazonaws.com/dev";

        // Token surface delegates to the shared EditorAuthToken so all tools read one token (no drift).
        public static string AccessToken => EditorAuthToken.AccessToken;
        public static DateTime AccessTokenExpiry => EditorAuthToken.AccessTokenExpiry;

        public static bool IsTokenValid => EditorAuthToken.IsValid;

        /// <summary>
        /// Request presigned S3 URLs for uploading catalog files and bundles
        /// </summary>
        public static async Task<CatalogUploadUrlsResponse> GetUploadUrlsAsync(string catalogId, string versionId, string[] bundleNames)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            var request = new CatalogUploadUrlsRequest
            {
                catalogId = catalogId,
                versionId = versionId,
                bundleNames = bundleNames
            };

            string jsonData = JsonUtility.ToJson(request);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest($"{BASE_URL}/users/me/catalogs/upload-urls", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        return JsonUtility.FromJson<CatalogUploadUrlsResponse>(www.downloadHandler.text);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to parse response: {e.Message}");
                    }
                }
                else
                {
                    throw new Exception($"Request failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Upload a file to S3 using presigned URL
        /// </summary>
        public static async Task UploadFileAsync(string presignedUrl, byte[] fileData)
        {
            using (UnityWebRequest www = UnityWebRequest.Put(presignedUrl, fileData))
            {
                www.SetRequestHeader("Content-Type", "application/octet-stream");

                await SendWebRequestAsync(www);

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Upload failed: {www.error} - {www.downloadHandler?.text}");
                }
            }
        }

        /// <summary>
        /// Confirm upload completion and finalize catalog publication
        /// </summary>
        public static async Task ConfirmUploadAsync(
            string catalogId,
            string versionId,
            string[] bundleNames,
            string name,
            string versionTag,
            CatalogMetadata metadata)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            var request = new CatalogConfirmRequest
            {
                catalogId = catalogId,
                versionId = versionId,
                bundleNames = bundleNames,
                name = name,
                versionTag = versionTag,
                metadata = metadata
            };

            string jsonData = JsonUtility.ToJson(request);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest($"{BASE_URL}/users/me/catalogs/confirm-upload", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result != UnityWebRequest.Result.Success)
                {
                    if (www.responseCode == 409)
                    {
                        throw new CatalogVersionExistsException("A version with this tag already exists for this catalog.");
                    }
                    throw new Exception($"Confirm upload failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Get list of all published catalogs
        /// </summary>
        public static async Task<CatalogSummary[]> GetAllCatalogsAsync()
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            string url = $"{BASE_URL}/users/me/catalogs";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<CatalogSummaryListResponse>(www.downloadHandler.text);
                        return response.catalogs ?? Array.Empty<CatalogSummary>();
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to parse response: {e.Message}");
                    }
                }
                else
                {
                    throw new Exception($"Request failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Delete a catalog by its ID
        /// </summary>
        public static async Task DeleteCatalogAsync(string catalogId)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            if (string.IsNullOrEmpty(catalogId))
            {
                throw new ArgumentException("Catalog ID is required", nameof(catalogId));
            }

            using (UnityWebRequest www = UnityWebRequest.Delete($"{BASE_URL}/users/me/catalogs/{catalogId}"))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
                www.downloadHandler = new DownloadHandlerBuffer();

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success || www.responseCode == 204)
                {
                    return;
                }
                else if (www.responseCode == 404)
                {
                    throw new Exception("Catalog not found");
                }
                else if (www.responseCode == 409)
                {
                    throw new CatalogInUseException("This catalog is currently in use and cannot be deleted.");
                }
                else
                {
                    throw new Exception($"Delete failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Rename a catalog
        /// </summary>
        public static async Task RenameCatalogAsync(string catalogId, string newName)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            if (string.IsNullOrEmpty(catalogId))
            {
                throw new ArgumentException("Catalog ID is required", nameof(catalogId));
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new ArgumentException("Catalog name is required", nameof(newName));
            }

            var request = new RenameCatalogRequest { name = newName.Trim() };
            string jsonData = JsonUtility.ToJson(request);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest($"{BASE_URL}/users/me/catalogs/{catalogId}", "PUT"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success || www.responseCode == 204)
                {
                    return;
                }
                else if (www.responseCode == 404)
                {
                    throw new Exception("Catalog not found");
                }
                else
                {
                    throw new Exception($"Rename failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Complete upload workflow: get URLs, upload all files, confirm
        /// </summary>
        public static async Task<CatalogSummary> UploadCatalogAsync(
            string catalogId,
            string versionId,
            string catalogName,
            string versionTag,
            byte[] binData,
            byte[] hashData,
            Dictionary<string, byte[]> bundleFiles,
            CatalogMetadata metadata,
            IProgress<(float progress, string message)> progress = null)
        {
            try
            {
                string[] bundleNames = bundleFiles.Keys.ToArray();

                progress?.Report((0.05f, "Requesting upload URLs..."));
                var uploadUrls = await GetUploadUrlsAsync(catalogId, versionId, bundleNames);

                // Prefer the caller-supplied ids (always correct for existing catalogs); fall back to the
                // server-assigned ids for brand-new catalogs where the caller passed null/empty.
                string resolvedCatalogId = !string.IsNullOrEmpty(catalogId) ? catalogId : uploadUrls.catalogId;
                string resolvedVersionId = !string.IsNullOrEmpty(versionId) ? versionId : uploadUrls.versionId;

                int totalFiles = 2 + bundleFiles.Count; // bin + hash + bundles
                int filesUploaded = 0;

                progress?.Report((0.1f, "Uploading content_catalog.bin..."));
                await UploadFileAsync(uploadUrls.uploadUrlBin, binData);
                filesUploaded++;

                progress?.Report((0.15f, "Uploading content_catalog.hash..."));
                await UploadFileAsync(uploadUrls.uploadUrlHash, hashData);
                filesUploaded++;

                // Upload each bundle file
                foreach (var bundleUrl in uploadUrls.uploadUrlBundles)
                {
                    if (bundleFiles.TryGetValue(bundleUrl.name, out byte[] bundleData))
                    {
                        float progressValue = 0.15f + (0.7f * filesUploaded / totalFiles);
                        progress?.Report((progressValue, $"Uploading {bundleUrl.name}... ({filesUploaded + 1}/{totalFiles})"));
                        await UploadFileAsync(bundleUrl.presignedUrl, bundleData);
                        filesUploaded++;
                    }
                }

                progress?.Report((0.9f, "Confirming upload..."));
                await ConfirmUploadAsync(
                    resolvedCatalogId,
                    resolvedVersionId,
                    bundleNames,
                    catalogName,
                    versionTag,
                    metadata);

                progress?.Report((0.95f, "Fetching catalog info..."));
                var catalogs = await GetAllCatalogsAsync();
                var summary = catalogs?.FirstOrDefault(c => c.catalogId == resolvedCatalogId);

                // The catalog list is read from an eventually-consistent DynamoDB GSI, so a brand-new
                // catalog can be missing from this immediate read. Fall back to a summary built from the
                // authoritative resolved id (returned directly by the upload-urls call) so the publish
                // never reports null — callers rely on this id to select/highlight the just-published catalog.
                return summary ?? new CatalogSummary
                {
                    catalogId = resolvedCatalogId,
                    name = catalogName,
                    latestVersionId = resolvedVersionId,
                    latestVersionTag = versionTag,
                    updatedAt = DateTime.UtcNow.ToString("o"), // just published → sorts to the top if merged client-side
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Upload workflow failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Create a new catalog (returns catalogId for use in upload flow)
        /// </summary>
        public static async Task<CreateCatalogResponse> CreateCatalogAsync(string name)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Catalog name is required", nameof(name));
            }

            var request = new CreateCatalogRequest { name = name.Trim() };
            string jsonData = JsonUtility.ToJson(request);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest($"{BASE_URL}/users/me/catalogs", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        return JsonUtility.FromJson<CreateCatalogResponse>(www.downloadHandler.text);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to parse response: {e.Message}");
                    }
                }
                else
                {
                    throw new Exception($"Create catalog failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Get a single catalog with full detail
        /// </summary>
        public static async Task<CatalogDetail> GetCatalogAsync(string catalogId)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            string url = $"{BASE_URL}/users/me/catalogs/{catalogId}";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        return JsonUtility.FromJson<CatalogDetail>(www.downloadHandler.text);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to parse response: {e.Message}");
                    }
                }
                else if (www.responseCode == 404)
                {
                    throw new Exception("Catalog not found");
                }
                else
                {
                    throw new Exception($"Request failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// List versions for a catalog
        /// </summary>
        public static async Task<CatalogVersionSummary[]> GetCatalogVersionsAsync(string catalogId)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            string url = $"{BASE_URL}/users/me/catalogs/{catalogId}/versions";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<CatalogVersionListResponse>(www.downloadHandler.text);
                        return response.versions ?? Array.Empty<CatalogVersionSummary>();
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to parse response: {e.Message}");
                    }
                }
                else if (www.responseCode == 404)
                {
                    throw new Exception("Catalog not found");
                }
                else
                {
                    throw new Exception($"Request failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Get a specific catalog version
        /// </summary>
        public static async Task<CatalogDetail> GetCatalogVersionAsync(string catalogId, string versionId)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            string url = $"{BASE_URL}/users/me/catalogs/{catalogId}/versions/{versionId}";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        return JsonUtility.FromJson<CatalogDetail>(www.downloadHandler.text);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to parse response: {e.Message}");
                    }
                }
                else if (www.responseCode == 404)
                {
                    throw new Exception("Catalog version not found");
                }
                else
                {
                    throw new Exception($"Request failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Delete a specific catalog version
        /// </summary>
        public static async Task DeleteCatalogVersionAsync(string catalogId, string versionId)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            if (string.IsNullOrEmpty(catalogId))
            {
                throw new ArgumentException("Catalog ID is required", nameof(catalogId));
            }

            if (string.IsNullOrEmpty(versionId))
            {
                throw new ArgumentException("Version ID is required", nameof(versionId));
            }

            using (UnityWebRequest www = UnityWebRequest.Delete($"{BASE_URL}/users/me/catalogs/{catalogId}?versionId={versionId}"))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
                www.downloadHandler = new DownloadHandlerBuffer();

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success || www.responseCode == 204)
                {
                    return;
                }
                else if (www.responseCode == 404)
                {
                    throw new Exception("Catalog version not found");
                }
                else if (www.responseCode == 409)
                {
                    throw new CatalogInUseException("This catalog version is currently in use and cannot be deleted.");
                }
                else
                {
                    throw new Exception($"Delete failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Set access token with expiry time
        /// </summary>
        public static void SetAccessToken(string token, DateTime expiryTime) => EditorAuthToken.Set(token, expiryTime);

        /// <summary>
        /// Clear stored access token
        /// </summary>
        public static void ClearToken() => EditorAuthToken.Clear();

        /// <summary>
        /// Helper method to convert UnityWebRequest to Task
        /// </summary>
        private static Task SendWebRequestAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                tcs.SetResult(true);
            };

            return tcs.Task;
        }
    }
}
