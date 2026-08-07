using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VirtualVenues.Editor.AssetPackPublisher
{
    /// <summary>
    /// State of a backend-backed ("Remote") editing session in the <see cref="AssetPackPublisherUI"/>:
    /// a published pack loaded from ff-api (GET /users/me/asset-packs/{id} + /assets) rather than from a
    /// local <see cref="AssetPackDefinition"/>. The backend stores assetKeys + metadata + built bundles —
    /// NOT prefabs — so rebinding rows to local prefabs happens here: first via a local draft whose packId
    /// matches (which also recovers icon settings and tags/categories the backend doesn't store), then by
    /// a unique prefab-name match on the assetKey's Id token.
    /// </summary>
    public class AssetPackRemoteSession
    {
        /// <summary>The pack as loaded (refreshed after each publish).</summary>
        public AssetPack pack;

        /// <summary>The server-side asset manifest at load time — the roster's baseline.</summary>
        public AssetMeta[] baselineAssets = Array.Empty<AssetMeta>();

        /// <summary>versionId at load time. Compared against a fresh GET before publish so a republish
        /// from another machine surfaces as an explicit "overwrite?" prompt instead of a silent clobber.</summary>
        public string loadedVersionId;

        /// <summary>Parsed pack_manifest.json (null = the version has none / download failed). Used to
        /// restore per-row exposeChildren when no local draft knows the flag.</summary>
        public MfPackManifest manifest;

        /// <summary>Local draft with the same packId (null if none in this project). The rebinding anchor:
        /// publish results are written through to it so future remote opens rebind instantly.</summary>
        public AssetPackDefinition matchedDraft;

        /// <summary>True when the loaded pack_manifest.json has an entry for this assetKey.</summary>
        public bool ManifestExposes(string assetKey)
        {
            if (manifest == null || manifest.entries == null || string.IsNullOrEmpty(assetKey)) { return false; }
            foreach (MfManifestEntry e in manifest.entries)
            {
                if (e != null && e.assetKey == assetKey) { return true; }
            }
            return false;
        }

        /// <summary>First AssetPackDefinition in the project whose packId matches (null if none).</summary>
        public static AssetPackDefinition FindMatchingDraft(string packId)
        {
            if (string.IsNullOrEmpty(packId)) { return null; }
            foreach (string guid in AssetDatabase.FindAssets("t:AssetPackDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<AssetPackDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && def.packId == packId) { return def; }
            }
            return null;
        }

        /// <summary>
        /// Best-effort prefab auto-bind for an assetKey ("{Type}_{Id}"): a prefab whose sanitized name
        /// (letters + digits, the same transform DeriveAssetKey applied at authoring time) equals the Id
        /// token. Returns null unless the match is UNIQUE — an ambiguous or wrong bind would silently
        /// republish the wrong content, so callers surface auto-bound rows for the creator to verify.
        /// Filters by filename before loading anything, so scanning a large project stays cheap.
        /// </summary>
        public static GameObject FindPrefabByAssetKey(string assetKey)
        {
            if (string.IsNullOrEmpty(assetKey)) { return null; }
            int us = assetKey.IndexOf('_');
            if (us < 0 || us == assetKey.Length - 1) { return null; }
            string idToken = assetKey.Substring(us + 1);

            string uniquePath = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (SanitizeId(Path.GetFileNameWithoutExtension(path)) != idToken) { continue; }
                if (uniquePath != null) { return null; } // ambiguous — require a manual rebind
                uniquePath = path;
            }
            return uniquePath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(uniquePath) : null;
        }

        /// <summary>Client-side auto-increment: bump the patch component of a semver string. Non-numeric /
        /// short versions normalize ("2" -> "2.0.1", garbage -> "1.0.1") — the backend never validates.</summary>
        public static string BumpPatch(string version)
        {
            int major = 1, minor = 0, patch = 0;
            if (!string.IsNullOrWhiteSpace(version))
            {
                string[] parts = version.Trim().Split('.');
                if (parts.Length > 0 && int.TryParse(parts[0], out int maj)) { major = maj; }
                if (parts.Length > 1) { int.TryParse(parts[1], out minor); }
                if (parts.Length > 2) { int.TryParse(parts[2], out patch); }
            }
            return $"{major}.{minor}.{patch + 1}";
        }

        // Same transform DeriveAssetKey applies to prefab names at authoring time (keep in sync with
        // AssetPackPublisherUI.SanitizeId).
        private static string SanitizeId(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) { if (char.IsLetterOrDigit(c)) { sb.Append(c); } }
            return sb.Length > 0 ? sb.ToString() : "Asset";
        }
    }
}
