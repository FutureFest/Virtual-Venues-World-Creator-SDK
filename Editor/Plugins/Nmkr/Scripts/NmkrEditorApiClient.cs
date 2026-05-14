using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// Raw-HTTP wrapper around the NMKR Studio ListProjects endpoint for
    /// Editor-only tooling. The VV SDK Editor assembly intentionally does not
    /// depend on com.nmkr.sdk; this client hits the endpoint directly via
    /// UnityWebRequest, mirroring the pattern in NmkrConnectionTester.
    /// </summary>
    public static class NmkrEditorApiClient
    {
        private const string PREPROD_URL = "https://studio-api.preprod.nmkr.io/v2";
        private const string MAINNET_URL = "https://studio-api.nmkr.io/v2";
        private const double CACHE_TTL_SECONDS = 60.0;

        /// <summary>
        /// One project as returned by GET /v2/ListProjects. Field names match
        /// the NMKR JSON (NftProjectsDetails) so UnityEngine.JsonUtility can
        /// populate them directly.
        /// </summary>
        [Serializable]
        public class ProjectSummary
        {
            public string uid;
            public string projectname;
            public string policyId;
            public long total;

            public string DisplayName => string.IsNullOrEmpty(projectname) ? uid : projectname;
        }

        public struct FetchResult
        {
            public bool Success;
            public string Error; // human-readable; null/empty on success
            public List<ProjectSummary> Projects;
        }

        [Serializable]
        private class ProjectsWrapper
        {
            public ProjectSummary[] items;
        }

        private class CacheEntry
        {
            public List<ProjectSummary> Projects;
            public double ExpiresAt;
        }

        private static readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

        /// <summary>
        /// Fetches the creator's NMKR projects. Results are cached per
        /// environment+customerId for <see cref="CACHE_TTL_SECONDS"/> seconds;
        /// pass <paramref name="bypassCache"/> = true to force a refetch.
        /// </summary>
        public static async Task<FetchResult> ListProjectsAsync(NmkrPluginConfig cfg, bool bypassCache = false)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
            {
                return new FetchResult
                {
                    Success = false,
                    Error = "NMKR is not configured (no API key).",
                    Projects = new List<ProjectSummary>(),
                };
            }

            string cacheKey = $"{cfg.Environment}:{cfg.CustomerId}";
            double now = EditorApplication.timeSinceStartup;
            if (!bypassCache
                && _cache.TryGetValue(cacheKey, out CacheEntry cached)
                && cached.ExpiresAt > now)
            {
                return new FetchResult { Success = true, Projects = cached.Projects };
            }

            string baseUrl = cfg.Environment == NmkrEnvironment.Mainnet ? MAINNET_URL : PREPROD_URL;
            string url = baseUrl + "/ListProjects";

            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", $"Bearer {cfg.ApiKey}");
            req.SetRequestHeader("accept", "text/plain");

            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone) { await Task.Yield(); }

            if (req.result != UnityWebRequest.Result.Success)
            {
                return new FetchResult
                {
                    Success = false,
                    Error = DescribeError(req),
                    Projects = new List<ProjectSummary>(),
                };
            }

            List<ProjectSummary> projects = ParseProjects(req.downloadHandler.text);
            _cache[cacheKey] = new CacheEntry { Projects = projects, ExpiresAt = now + CACHE_TTL_SECONDS };
            return new FetchResult { Success = true, Projects = projects };
        }

        /// <summary>Drops all cached project lists.</summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        private static List<ProjectSummary> ParseProjects(string rawJson)
        {
            List<ProjectSummary> result = new List<ProjectSummary>();
            if (string.IsNullOrEmpty(rawJson)) { return result; }

            // NMKR returns a top-level JSON array; JsonUtility only parses
            // objects, so wrap it before deserializing.
            string wrapped = "{\"items\":" + rawJson + "}";
            try
            {
                ProjectsWrapper parsed = JsonUtility.FromJson<ProjectsWrapper>(wrapped);
                if (parsed?.items != null) { result.AddRange(parsed.items); }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NmkrEditorApiClient] Failed to parse ListProjects response: {e.Message}");
            }

            return result;
        }

        private static string DescribeError(UnityWebRequest req)
        {
            long code = req.responseCode;
            if (code == 401) { return "Unauthorized (401) — check the API key in NMKR Settings."; }
            if (code == 403) { return "Forbidden (403) — the API key lacks permission to list projects."; }
            if (code == 429) { return "Rate limited (429) — wait a moment, then refresh."; }
            string err = string.IsNullOrEmpty(req.error) ? "Unknown error" : req.error;
            return $"{err} (HTTP {code}).";
        }
    }
}
