using System;
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
    }

    [Serializable]
    public class UploadUrlsResponse
    {
        public string uploadUrlUms;
        public string uploadUrlUpc;
    }

    [Serializable]
    public class ConfirmUploadRequest
    {
        public string umsName;
        public string upcName;
    }

    [Serializable]
    public class World
    {
        public string umsUrl;
        public string upcUrl;
        public string updatedAt;
    }

    [Serializable]
    public class WorldListResponse
    {
        public World[] worlds;
    }

    public static class WorldsPublisherApi
    {
        private const string BASE_URL = "https://jsxf2xqpn4.execute-api.us-east-1.amazonaws.com/dev";

        public static string AccessToken { get; set; }
        public static DateTime AccessTokenExpiry { get; set; }

        public static bool IsTokenValid => !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < AccessTokenExpiry;

        /// <summary>
        /// Request presigned S3 URLs for uploading UMS and UPC bundles
        /// </summary>
        public static async Task<UploadUrlsResponse> GetUploadUrlsAsync(string umsName, string upcName)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            var request = new UploadUrlsRequest
            {
                umsName = umsName,
                upcName = upcName
            };

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
        public static async Task ConfirmUploadAsync(string umsName, string upcName)
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            var request = new ConfirmUploadRequest
            {
                umsName = umsName,
                upcName = upcName
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
        /// Get the current user's published world
        /// </summary>
        public static async Task<World> GetCurrentWorldAsync()
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            using (UnityWebRequest www = UnityWebRequest.Get($"{BASE_URL}/worlds/current"))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        return JsonUtility.FromJson<World>(www.downloadHandler.text);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to parse response: {e.Message}");
                    }
                }
                else if (www.responseCode == 404)
                {
                    return null; // No published world
                }
                else
                {
                    throw new Exception($"Request failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Delete the current user's published world
        /// </summary>
        public static async Task DeleteCurrentWorldAsync()
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            using (UnityWebRequest www = UnityWebRequest.Delete($"{BASE_URL}/worlds/current"))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success || www.responseCode == 204)
                {
                    return; // Success
                }
                else if (www.responseCode == 404)
                {
                    throw new Exception("No world to delete");
                }
                else
                {
                    throw new Exception($"Delete failed: {www.error} - {www.downloadHandler.text}");
                }
            }
        }

        /// <summary>
        /// Get list of all published worlds
        /// </summary>
        public static async Task<World[]> GetAllWorldsAsync()
        {
            if (!IsTokenValid)
            {
                throw new InvalidOperationException("Access token is invalid or expired");
            }

            using (UnityWebRequest www = UnityWebRequest.Get($"{BASE_URL}/worlds"))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

                await SendWebRequestAsync(www);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<WorldListResponse>(www.downloadHandler.text);
                        return response.worlds;
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
            IProgress<string> progress = null)
        {
            try
            {
                progress?.Report("Requesting upload URLs...");
                var uploadUrls = await GetUploadUrlsAsync(umsName, upcName);

                progress?.Report("Uploading UMS bundle...");
                await UploadBundleAsync(uploadUrls.uploadUrlUms, umsData);

                progress?.Report("Uploading UPC bundle...");
                await UploadBundleAsync(uploadUrls.uploadUrlUpc, upcData);

                progress?.Report("Confirming upload...");
                await ConfirmUploadAsync(umsName, upcName);

                progress?.Report("Fetching world info...");
                return await GetCurrentWorldAsync();
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