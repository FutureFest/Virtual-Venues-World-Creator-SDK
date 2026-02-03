using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    public class CatalogVersion
    {
        public string versionId;
        public string versionTag;
        public string createdAt;
        public CatalogMetadata metadata;
    }

    [Serializable]
    public class Catalog
    {
        public string catalogId;
        public string name;
        public string userId;
        public string updatedAt;
        public CatalogVersion[] versions;
    }

    [Serializable]
    public class CatalogListResponse
    {
        public Catalog[] catalogs;
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

        public static string AccessToken { get; set; }
        public static DateTime AccessTokenExpiry { get; set; }

        public static bool IsTokenValid => !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < AccessTokenExpiry;

        /// <summary>
        /// Request presigned S3 URLs for uploading catalog files and bundles
        /// </summary>
        public static async Task<CatalogUploadUrlsResponse> GetUploadUrlsAsync(string[] bundleNames, string catalogId = null, string versionId = null)
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
        public static async Task<Catalog[]> GetAllCatalogsAsync()
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
                        var response = JsonUtility.FromJson<CatalogListResponse>(www.downloadHandler.text);
                        return response.catalogs ?? Array.Empty<Catalog>();
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
        public static async Task<Catalog> UploadCatalogAsync(
            string catalogName,
            string versionTag,
            byte[] binData,
            byte[] hashData,
            Dictionary<string, byte[]> bundleFiles,
            CatalogMetadata metadata,
            string existingCatalogId = null,
            IProgress<(float progress, string message)> progress = null)
        {
            try
            {
                string[] bundleNames = bundleFiles.Keys.ToArray();

                progress?.Report((0.05f, "Requesting upload URLs..."));
                var uploadUrls = await GetUploadUrlsAsync(bundleNames, existingCatalogId);

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
                    uploadUrls.catalogId,
                    uploadUrls.versionId,
                    bundleNames,
                    catalogName,
                    versionTag,
                    metadata);

                progress?.Report((0.95f, "Fetching catalog info..."));
                var catalogs = await GetAllCatalogsAsync();
                return catalogs?.FirstOrDefault(c => c.catalogId == uploadUrls.catalogId);
            }
            catch (Exception ex)
            {
                throw new Exception($"Upload workflow failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Set access token with expiry time
        /// </summary>
        public static void SetAccessToken(string token, DateTime expiryTime)
        {
            AccessToken = token;
            AccessTokenExpiry = expiryTime;
        }

        /// <summary>
        /// Clear stored access token
        /// </summary>
        public static void ClearToken()
        {
            AccessToken = null;
            AccessTokenExpiry = DateTime.MinValue;
        }

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
