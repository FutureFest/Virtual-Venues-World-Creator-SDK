using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Auth0;
using UnityEngine;
using UnityEngine.Networking;
using VirtualVenues.Editor.Publishing; // AddressablesCatalogBuilder (BuildResult / BuildOutputFile)
// AssetMeta / PackageMetadata / LibraryPackage / UploadUrl live in this assembly (PublisherDtos.cs)

namespace VirtualVenues.Editor.AssetPackPublisher
{
    #region ff-api asset-pack DTOs (JsonUtility-safe; camelCase fields matching docs/uwe/openapi.yaml)

    [Serializable]
    public class AssetPackCreateRequest
    {
        public string name;
        public string version;
        public int price;
    }

    /// <summary>POST /users/me/asset-packs response (also the confirm-upload assetPack). metadata may be null on a draft.</summary>
    [Serializable]
    public class AssetPack
    {
        public string id;
        public string name;
        public string version;
        public int price;
        public string ownerUserId;
        public string catalogId;       // empty until confirm
        public string versionId;       // empty until confirm
        public string contentBaseUrl;  // empty until confirm
        public PackageMetadata metadata;
    }

    [Serializable]
    public class UploadUrlsRequest
    {
        public string versionId;
        public string[] assetKeys; // S3 OBJECT NAMES (content_catalog.bin/.hash/<bundle>.bundle) — NOT Type_Id keys
    }

    /// <summary>One presigned upload target. assetKey is the S3 object name; method is "PUT".</summary>
    [Serializable]
    public class UploadUrl
    {
        public string assetKey;
        public string url;
        public string method;
        public string expiresAt;
    }

    [Serializable]
    public class UploadUrlList
    {
        public UploadUrl[] uploadUrls;
    }

    [Serializable]
    public class PackageInfo
    {
        public string name;
        public string version;
        public int price;
    }

    [Serializable]
    public class ListingInfo
    {
        public string[] tags;
        public string[] categories;
        public string visibility; // "public"
    }

    [Serializable]
    public class ConfirmUploadRequest
    {
        public string versionId;
        public string[] objectNames;   // S3 object names — MUST equal the upload-urls set (backend HeadObjects each)
        public AssetMeta[] assets;     // assetKey here = Type_Id content key (distinct from objectNames)
        public PackageInfo package;
        public ListingInfo listing;
        public PackageMetadata metadata;
    }

    [Serializable]
    public class Listing
    {
        public string id;
        public string packageId;
        public string visibility;
    }

    [Serializable]
    public class ConfirmUploadResponse
    {
        public AssetPack assetPack;
        public Listing listing;
    }

    [Serializable]
    public class AddToLibraryRequest
    {
        public string packageId;
    }

    /// <summary>GET /users/me/asset-packs response.</summary>
    [Serializable]
    public class AssetPackList
    {
        public AssetPack[] assetPacks;
    }

    /// <summary>GET /users/me/asset-packs/{id}/assets response (the stored asset manifest).</summary>
    [Serializable]
    public class AssetList
    {
        public AssetMeta[] assets;
    }

    /// <summary>Result of the full publish + add-to-library flow.</summary>
    public class PublishResult
    {
        public AssetPack assetPack;
        public Listing listing;
        public LibraryPackage library; // reuses the runtime LibraryPackage DTO (same shape)

        /// <summary>The asset metas as published — thumbnailUrl is filled in during the upload step.</summary>
        public AssetMeta[] assets;
    }

    #endregion

    /// <summary>
    /// ff-api client for the creator Asset-Pack flow (docs/uwe/openapi.yaml §4 + add-to-library §6):
    /// create-pack -> upload-urls -> PUT presigned -> confirm-upload -> add-to-library. Editor-only
    /// (UnityWebRequest + the Addressables build engine). Bearer auth via the shared <see cref="EditorAuthToken"/>
    /// (the SAME Auth0 token + ff-api host the avatar publisher already uses — only the path differs).
    ///
    /// contentBaseUrl is baked into the bundles at build time, so it is derived from the presigned
    /// content_catalog.bin URL (guaranteed to match the bucket the backend presigns/HeadObjects against) rather
    /// than guessing FF_BUCKET_URL. Mirrors AvatarPublisherApi's PUT + TCS patterns.
    /// </summary>
    public static class AssetPackPublisherApi
    {
        // Same ff-api host as the avatar/world tooling (only the /users/me/asset-packs/* paths differ).
        private const string BASE_URL = "https://jsxf2xqpn4.execute-api.us-east-1.amazonaws.com/dev";

        private const string CatalogBinObject = "content_catalog.bin";
        private const string CatalogHashObject = "content_catalog.hash";

        public static string AccessToken => EditorAuthToken.AccessToken;
        public static bool IsTokenValid => EditorAuthToken.IsValid;
        public static void SetAccessToken(string token, DateTime expiry) => EditorAuthToken.Set(token, expiry);
        public static void ClearToken() => EditorAuthToken.Clear();

        // -------------------------------------------------------------------------------------------
        // Endpoints.
        // -------------------------------------------------------------------------------------------

        /// <summary>POST /users/me/asset-packs — create a draft pack.</summary>
        public static async Task<AssetPack> CreatePackAsync(string name, string version, int price)
        {
            RequireToken();
            var body = new AssetPackCreateRequest { name = name, version = string.IsNullOrEmpty(version) ? "1.0.0" : version, price = price };
            return await PostJsonAsync<AssetPack>($"{BASE_URL}/users/me/asset-packs", body);
        }

        /// <summary>POST /users/me/asset-packs/{id}/upload-urls — presign the given S3 object names.</summary>
        public static async Task<UploadUrl[]> GetUploadUrlsAsync(string packId, string versionId, string[] objectNames)
        {
            RequireToken();
            var body = new UploadUrlsRequest { versionId = versionId, assetKeys = objectNames };
            var list = await PostJsonAsync<UploadUrlList>($"{BASE_URL}/users/me/asset-packs/{Uri.EscapeDataString(packId)}/upload-urls", body);
            return list != null && list.uploadUrls != null ? list.uploadUrls : Array.Empty<UploadUrl>();
        }

        /// <summary>HTTP PUT a file's bytes to a presigned S3 URL (NO Authorization header — the URL is signed).</summary>
        public static async Task UploadFileAsync(string presignedUrl, byte[] fileData)
        {
            using (UnityWebRequest www = UnityWebRequest.Put(presignedUrl, fileData))
            {
                www.SetRequestHeader("Content-Type", "application/octet-stream");
                await SendWebRequestAsync(www);
                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Upload failed: {www.error} - {SafeBody(www)}");
                }
            }
        }

        /// <summary>POST /users/me/asset-packs/{id}/confirm-upload — finalize + create the marketplace listing.</summary>
        public static async Task<ConfirmUploadResponse> ConfirmUploadAsync(string packId, ConfirmUploadRequest request)
        {
            RequireToken();
            return await PostJsonAsync<ConfirmUploadResponse>($"{BASE_URL}/users/me/asset-packs/{Uri.EscapeDataString(packId)}/confirm-upload", request);
        }

        /// <summary>POST /users/me/library — add a published pack to the caller's library (free + idempotent).</summary>
        public static async Task<LibraryPackage> AddToLibraryAsync(string packageId)
        {
            RequireToken();
            var body = new AddToLibraryRequest { packageId = packageId };
            return await PostJsonAsync<LibraryPackage>($"{BASE_URL}/users/me/library", body);
        }

        /// <summary>GET /users/me/asset-packs — list the caller's packs. Supports a future "import from account"
        /// recovery flow (rebuild a local AssetPackDefinition when its .asset was lost; prefab links must be
        /// re-bound by hand since the backend stores assetKeys, not Unity GUIDs).</summary>
        public static async Task<AssetPack[]> GetAllPacksAsync()
        {
            RequireToken();
            var list = await GetJsonAsync<AssetPackList>($"{BASE_URL}/users/me/asset-packs");
            return list != null && list.assetPacks != null ? list.assetPacks : Array.Empty<AssetPack>();
        }

        /// <summary>GET /users/me/asset-packs/{id}/assets — the pack's stored asset manifest
        /// (assetKey/displayName/category/kind/thumbnailUrl/bounds).</summary>
        public static async Task<AssetMeta[]> GetPackAssetsAsync(string packId)
        {
            RequireToken();
            var list = await GetJsonAsync<AssetList>($"{BASE_URL}/users/me/asset-packs/{Uri.EscapeDataString(packId)}/assets");
            return list != null && list.assets != null ? list.assets : Array.Empty<AssetMeta>();
        }

        // -------------------------------------------------------------------------------------------
        // Full publish orchestrator: create -> reserve -> build -> upload -> confirm -> add-to-library.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Build the catalog of <paramref name="entries"/> and publish it to ff-api, then add it to the caller's
        /// library so the UWE consume grid shows it. The caller should wrap this in
        /// EditorApplication.LockReloadAssemblies/Unlock (the build switches the active build target).
        /// </summary>
        public static async Task<PublishResult> PublishAsync(
            AssetPackCatalogBuilder builder,
            IList<AssetPackCatalogBuilder.AssetPackEntry> entries,
            AssetMeta[] assets,
            string packName,
            string version,
            int price,
            string[] tags,
            string[] categories,
            string visibility,
            string existingPackId = null,
            IReadOnlyDictionary<string, byte[]> thumbnails = null,
            IProgress<(float progress, string message)> progress = null,
            string packManifestJson = null)
        {
            if (builder == null) { throw new ArgumentNullException(nameof(builder)); }
            if (entries == null || entries.Count == 0) { throw new ArgumentException("At least one asset is required."); }
            RequireToken();

            // 1) Reuse the existing pack (UPDATE) or create a fresh draft. Reusing the packId keeps the
            //    marketplace pack's identity across updates instead of minting a new pack every publish.
            AssetPack pack;
            string packId;
            if (!string.IsNullOrEmpty(existingPackId))
            {
                progress?.Report((0.02f, "Updating existing asset pack..."));
                packId = existingPackId;
                pack = new AssetPack { id = packId, name = packName, version = version, price = price };
            }
            else
            {
                progress?.Report((0.02f, "Creating asset pack..."));
                pack = await CreatePackAsync(packName, version, price);
                if (pack == null || string.IsNullOrEmpty(pack.id)) { throw new Exception("Create pack returned no id."); }
                packId = pack.id;
            }

            // 2) versionId BEFORE the build (the remote load path is baked into the bundles at build time).
            string versionId = AddressablesCatalogBuilder.GenerateVersionId();
            // Bake a per-publish-unique bundle identity (fresh group GUID + versionId-suffixed names) so
            // two packs/versions can coexist in one Unity session ("same files already loaded" fix).
            builder.SetBuildIdentity(versionId);

            // 3) Reserve content_catalog.bin to DERIVE the deterministic contentBaseUrl from its presigned URL
            //    (matches the bucket the backend presigns/HeadObjects against — no FF_BUCKET_URL guessing).
            progress?.Report((0.06f, "Reserving upload location..."));
            UploadUrl[] preUrls = await GetUploadUrlsAsync(packId, versionId, new[] { CatalogBinObject });
            string binPresigned = FindUrl(preUrls, CatalogBinObject);
            if (string.IsNullOrEmpty(binPresigned)) { throw new Exception("upload-urls did not return a content_catalog.bin URL."); }
            string contentBaseUrl = DeriveContentBaseUrl(binPresigned);
            if (string.IsNullOrEmpty(contentBaseUrl)) { throw new Exception("Could not derive contentBaseUrl from the presigned URL."); }

            // 4) Build the isolated catalog with that load path baked in. BuildForWebGPU pre-cleans the
            //    shared ContentData output folder (hard-fails on a locked stale file).
            builder.SetupAssetGroup(entries);
            builder.SetRemoteLoadPath(contentBaseUrl);
            progress?.Report((0.12f, "Building Addressables catalog..."));
            AddressablesCatalogBuilder.BuildResult build = builder.BuildForWebGPU(progress);
            if (build == null || !build.Success) { throw new Exception($"Catalog build failed: {build?.Error}"); }

            // 5) Map build output -> S3 object names + bytes (catalog_<ver>.bin/.hash -> content_catalog.*; bundles keep names; skip .json).
            //    Bundles are filtered to THIS build's outputs — bundle names are unique per publish now,
            //    so a stale file that somehow survived the pre-clean must never ride along.
            var builtBundles = new HashSet<string>(build.BundleFilePaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var objectBytes = new Dictionary<string, byte[]>();
            foreach (AddressablesCatalogBuilder.BuildOutputFile f in builder.GetBuildOutputFiles())
            {
                if (f.IsCatalog && f.FileName.EndsWith(".bin")) { objectBytes[CatalogBinObject] = File.ReadAllBytes(f.FilePath); }
                else if (f.IsCatalog && f.FileName.EndsWith(".hash")) { objectBytes[CatalogHashObject] = File.ReadAllBytes(f.FilePath); }
                else if (f.IsBundle && builtBundles.Contains(Path.GetFullPath(f.FilePath))) { objectBytes[f.FileName] = File.ReadAllBytes(f.FilePath); }
                // catalog_<ver>.json, stale bundles, and anything else is not uploaded.
            }
            if (!objectBytes.ContainsKey(CatalogBinObject) || !objectBytes.ContainsKey(CatalogHashObject))
            {
                throw new Exception("Build output is missing content_catalog.bin/.hash.");
            }

            // 5b) Add per-asset thumbnails as extra objects ("thumb_<assetKey>.png") under the SAME version
            //     prefix, and stamp each asset's public thumbnailUrl (contentBaseUrl keeps its trailing slash).
            //     The backend presigns + HeadObject-verifies arbitrary object names, so thumbnails ride the
            //     same upload/confirm path with no backend change and resolve publicly like the bundles do.
            if (thumbnails != null && thumbnails.Count > 0)
            {
                var assetByKey = new Dictionary<string, AssetMeta>();
                if (assets != null)
                {
                    foreach (AssetMeta m in assets)
                    {
                        if (m != null && !string.IsNullOrEmpty(m.assetKey)) { assetByKey[m.assetKey] = m; }
                    }
                }
                foreach (KeyValuePair<string, byte[]> kv in thumbnails)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || kv.Value.Length == 0) { continue; }
                    string objectName = $"thumb_{kv.Key}.png";
                    objectBytes[objectName] = kv.Value;
                    if (assetByKey.TryGetValue(kv.Key, out AssetMeta meta)) { meta.thumbnailUrl = contentBaseUrl + objectName; }
                }
            }

            // 5c) Composite manifest (pack_manifest.json): ONE extra object per version describing the
            //     "Expose children" items so the World Editor explodes them into real child objects at
            //     drop time. Same zero-backend ride-along as the thumbnails above; the UWE fetches
            //     {contentBaseUrl}pack_manifest.json once per pack (404 = no composites).
            if (!string.IsNullOrEmpty(packManifestJson))
            {
                objectBytes["pack_manifest.json"] = System.Text.Encoding.UTF8.GetBytes(packManifestJson);
            }

            string[] objectNames = objectBytes.Keys.ToArray();

            // 6) Presign all objects (same versionId) and PUT each.
            progress?.Report((0.6f, "Requesting upload URLs..."));
            UploadUrl[] urls = await GetUploadUrlsAsync(packId, versionId, objectNames);
            int total = urls.Length;
            int done = 0;
            foreach (UploadUrl u in urls)
            {
                if (u == null || string.IsNullOrEmpty(u.url) || !objectBytes.TryGetValue(u.assetKey, out byte[] bytes)) { continue; }
                progress?.Report((0.6f + 0.25f * (done / (float)Math.Max(1, total)), $"Uploading {u.assetKey} ({done + 1}/{total})..."));
                await UploadFileAsync(u.url, bytes);
                done++;
            }

            // 7) Confirm-upload (publishes + creates the public listing).
            progress?.Report((0.9f, "Confirming upload..."));
            var metadata = new PackageMetadata
            {
                unityVersion = Application.unityVersion,
                bundleVersion = string.IsNullOrEmpty(version) ? "1.0.0" : version,
                buildTarget = "WebGPU",
            };
            var confirmReq = new ConfirmUploadRequest
            {
                versionId = versionId,
                objectNames = objectNames,
                assets = assets ?? Array.Empty<AssetMeta>(),
                package = new PackageInfo { name = packName, version = string.IsNullOrEmpty(version) ? "1.0.0" : version, price = price },
                listing = new ListingInfo
                {
                    tags = tags ?? Array.Empty<string>(),
                    categories = categories ?? Array.Empty<string>(),
                    visibility = string.IsNullOrEmpty(visibility) ? "public" : visibility,
                },
                metadata = metadata,
            };
            ConfirmUploadResponse confirm = await ConfirmUploadAsync(packId, confirmReq);

            // 8) Add to the caller's library.
            progress?.Report((0.97f, "Adding to your library..."));
            string libraryPackageId = confirm != null && confirm.assetPack != null && !string.IsNullOrEmpty(confirm.assetPack.id)
                ? confirm.assetPack.id
                : packId;
            LibraryPackage library = await AddToLibraryAsync(libraryPackageId);

            progress?.Report((1f, "Published!"));
            return new PublishResult { assetPack = confirm?.assetPack ?? pack, listing = confirm?.listing, library = library, assets = assets };
        }

        // -------------------------------------------------------------------------------------------
        // Helpers.
        // -------------------------------------------------------------------------------------------

        // Strip the query string and the trailing "content_catalog.bin" from a presigned URL to get the
        // version-pinned contentBaseUrl (keeps the trailing slash: runtime requests {base}content_catalog.bin).
        private static string DeriveContentBaseUrl(string presignedBinUrl)
        {
            if (string.IsNullOrEmpty(presignedBinUrl)) { return null; }
            int q = presignedBinUrl.IndexOf('?');
            string noQuery = q >= 0 ? presignedBinUrl.Substring(0, q) : presignedBinUrl;
            int lastSlash = noQuery.LastIndexOf('/');
            if (lastSlash < 0) { return null; }
            return noQuery.Substring(0, lastSlash + 1);
        }

        private static string FindUrl(UploadUrl[] urls, string objectName)
        {
            if (urls == null) { return null; }
            foreach (UploadUrl u in urls) { if (u != null && u.assetKey == objectName) { return u.url; } }
            return null;
        }

        private static void RequireToken()
        {
            if (!IsTokenValid) { throw new InvalidOperationException("Not signed in (the ff-api access token is missing or expired)."); }
        }

        private static string SafeBody(UnityWebRequest www)
        {
            return www != null && www.downloadHandler != null ? www.downloadHandler.text : string.Empty;
        }

        private static async Task<T> PostJsonAsync<T>(string url, object body) where T : class
        {
            byte[] raw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(raw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
                await SendWebRequestAsync(www);

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"{url} failed ({www.responseCode}): {www.error} - {SafeBody(www)}");
                }
                try { return JsonUtility.FromJson<T>(www.downloadHandler.text); }
                catch (Exception e) { throw new Exception($"Failed to parse response from {url}: {e.Message}"); }
            }
        }

        private static async Task<T> GetJsonAsync<T>(string url) where T : class
        {
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
                await SendWebRequestAsync(www);

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"{url} failed ({www.responseCode}): {www.error} - {SafeBody(www)}");
                }
                try { return JsonUtility.FromJson<T>(www.downloadHandler.text); }
                catch (Exception e) { throw new Exception($"Failed to parse response from {url}: {e.Message}"); }
            }
        }

        // UnityWebRequest -> Task. A bounded timeout + exception surfacing is critical here: the whole publish
        // runs inside EditorApplication.LockReloadAssemblies(), so a request that NEVER completes (stalled
        // socket / black-holed proxy) would leave the await pending forever and the editor stuck unable to
        // recompile. timeout makes op.completed fire (as a ConnectionError, handled by the result checks).
        private static Task SendWebRequestAsync(UnityWebRequest request)
        {
            if (request.timeout <= 0) { request.timeout = 120; }
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                var op = request.SendWebRequest();
                op.completed += _ => tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }
    }
}
