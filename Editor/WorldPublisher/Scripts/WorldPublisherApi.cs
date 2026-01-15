using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace WorldPublisher
{
    [Serializable]
    public class UploadUrlsRequest
    {
        public string umsName;
        public string upcName;
        public string worldName;
    }

    [Serializable]
    public class UploadUrlsResponse
    {
        public string worldId;
        public string uploadUrlUms;
        public string uploadUrlUpc;
    }

    [Serializable]
    public class ConfirmUploadRequest
    {
        public string worldId;
        public string umsName;
        public string upcName;
        public string worldName;
    }

    [Serializable]
    public class World
    {
        public string worldId;
        public string worldName;
        public string userId;
        public string updatedAt;
        public bool isDefault;
    }

    [Serializable]
    public class WorldListResponse
    {
        public World[] worlds;
    }

    [Serializable]
    public class RenameWorldRequest
    {
        public string worldName;
    }

    public class WorldInUseException : Exception
    {
        public WorldInUseException(string message) : base(message) { }
    }

    public static class WorldPublisherApi
    {
        private const string BASE_URL = "https://jsxf2xqpn4.execute-api.us-east-1.amazonaws.com/dev";

        public static string AccessToken { get; set; }
        public static DateTime AccessTokenExpiry { get; set; }

        public static bool IsTokenValid => !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < AccessTokenExpiry;

        /// <summary>
        /// Request presigned S3 URLs for uploading UMS and UPC bundles
        /// </summary>
        public static async Task<UploadUrlsResponse> GetUploadUrlsAsync(string umsName, string upcName, string worldName = null)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            var request = new UploadUrlsRequest
            {
                umsName = umsName,
                upcName = upcName,
                worldName = worldName
            };
            
            Debug.Log("world name" + request.worldName);

            string jsonData = JsonUtility.ToJson(request);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest($"{BASE_URL}/worlds/upload-urls", "POST"))
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
                        return JsonUtility.FromJson<UploadUrlsResponse>(www.downloadHandler.text);
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
        /// Upload a bundle file to S3 using presigned URL
        /// </summary>
        public static async Task UploadBundleAsync(string presignedUrl, byte[] fileData)
        {
            using (UnityWebRequest www = UnityWebRequest.Put(presignedUrl, fileData))
            {
                www.SetRequestHeader("Content-Type", "application/octet-stream");

                await SendWebRequestAsync(www);

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Upload failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Confirm upload completion and finalize world publication
        /// </summary>
        public static async Task ConfirmUploadAsync(string worldId, string umsName, string upcName, string worldName = null)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            var request = new ConfirmUploadRequest
            {
                worldId = worldId,
                umsName = umsName,
                upcName = upcName,
                worldName = worldName
            };

            string jsonData = JsonUtility.ToJson(request);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest($"{BASE_URL}/worlds/confirm-upload", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Confirm upload failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Delete a world by its ID
        /// </summary>
        public static async Task DeleteWorldAsync(string worldId)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            if (string.IsNullOrEmpty(worldId))
            {
                throw new ArgumentException("World ID is required", nameof(worldId));
            }

            using (UnityWebRequest www = UnityWebRequest.Delete($"{BASE_URL}/worlds/{worldId}"))
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
                    throw new Exception("World not found");
                }
                else if (www.responseCode == 409)
                {
                    throw new WorldInUseException("This world is currently in use by an event and cannot be deleted.");
                }
                else
                {
                    throw new Exception($"Delete failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Rename a world
        /// </summary>
        public static async Task RenameWorldAsync(string worldId, string newName)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            if (string.IsNullOrEmpty(worldId))
            {
                throw new ArgumentException("World ID is required", nameof(worldId));
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new ArgumentException("World name is required", nameof(newName));
            }

            var request = new RenameWorldRequest { worldName = newName.Trim() };
            string jsonData = JsonUtility.ToJson(request);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest($"{BASE_URL}/vv/worlds/{worldId}", "PUT"))
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
                    throw new Exception("World not found");
                }
                else
                {
                    throw new Exception($"Rename failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Get list of all published worlds (excludes default/system worlds)
        /// </summary>
        public static async Task<World[]> GetAllWorldsAsync()
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            string url = $"{BASE_URL}/vv/worlds";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<WorldListResponse>(www.downloadHandler.text);
                        // Filter out default/system worlds - only show user's own worlds
                        return response.worlds?.Where(w => !w.isDefault).ToArray() ?? Array.Empty<World>();
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
        /// Complete upload workflow: get URLs, upload both bundles, confirm
        /// </summary>
        public static async Task<World> UploadWorldAsync(string umsName, string upcName, byte[] umsData, byte[] upcData,
            string worldName = null, IProgress<string> progress = null)
        {
            try
            {
                progress?.Report("Requesting upload URLs...");
                var uploadUrls = await GetUploadUrlsAsync(umsName, upcName, worldName);

                progress?.Report("Uploading UMS bundle...");
                await UploadBundleAsync(uploadUrls.uploadUrlUms, umsData);

                progress?.Report("Uploading UPC bundle...");
                await UploadBundleAsync(uploadUrls.uploadUrlUpc, upcData);

                progress?.Report("Confirming upload...");
                await ConfirmUploadAsync(uploadUrls.worldId, umsName, upcName, worldName);

                progress?.Report("Fetching world info...");
                var worlds = await GetAllWorldsAsync();
                return worlds?.FirstOrDefault(w => w.worldId == uploadUrls.worldId);
            }
            catch (Exception ex)
            {
                throw new Exception($"Upload workflow failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Set access token with expiry time
        /// </summary>
        public static void SetAccessToken(string token, System.DateTime dataTime)
        {
            AccessToken = token;
            AccessTokenExpiry = dataTime;
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
        /// Generate unique filename using SHA256 hash of content
        /// </summary>
        public static string GenerateUniqueFilename(string prefix, byte[] content)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(content);
                string hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return $"{prefix}-{hashString.Substring(0, 8)}.bundle";
            }
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