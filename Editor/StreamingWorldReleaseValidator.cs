using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VirtualVenues.WorldCreator;

namespace VirtualVenues.Editor
{
    /// <summary>Checks actual Unity bundle metadata; does not activate scenes or validate browser rendering.</summary>
    public static class StreamingWorldReleaseValidator
    {
        [Serializable] public sealed class BundleAudit
        {
            public string name;
            public string[] scenePaths;
            public bool crcPassed;
            public bool hashMatched;
            public long bytes;
        }
        [Serializable] public sealed class ReleaseAudit
        {
            public string unityVersion;
            public string result;
            public BundleAudit[] bundles;
        }
        [MenuItem("VirtualVenues/Worlds/Validate streaming release (pilot)")]
        private static void PickRelease()
        {
            string path = EditorUtility.OpenFilePanel("Select world.streaming.json", "", "json");
            if (string.IsNullOrEmpty(path)) { return; }
            Validate(Path.GetDirectoryName(path));
        }
        public static ReleaseAudit Validate(string directory) => Validate(directory, "WebGL");

        public static ReleaseAudit Validate(string directory, string expectedTarget)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Validate outside Play mode with the world unloaded.");
            string folder = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            var manifest = JsonUtility.FromJson<WorldStreamingManifest>(File.ReadAllText(Path.Combine(folder, "world.streaming.json")));
            manifest.Validate(expectedTarget);
            var root = AssetBundle.LoadFromFile(Path.Combine(folder, Path.GetFileName(folder)));
            if (root == null) throw new InvalidOperationException("Unable to load the Unity build manifest.");
            try
            {
                var built = root.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                if (built == null) throw new InvalidOperationException("Missing Unity AssetBundleManifest asset.");
                var rows = manifest.bundles.Select(entry =>
                {
                    var bundle = AssetBundle.LoadFromFile(Path.Combine(folder, entry.file), entry.crc);
                    if (bundle == null) throw new InvalidOperationException("Bundle CRC/load failed: " + entry.name);
                    try
                    {
                        var paths = bundle.GetAllScenePaths();
                        if (!string.IsNullOrEmpty(entry.scene) && !paths.Any(p => string.Equals(p, entry.scene, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("Declared scene missing: " + entry.name);
                        foreach (string extra in entry.additionalScenes ?? Array.Empty<string>())
                            if (!paths.Any(p => string.Equals(p, extra, StringComparison.OrdinalIgnoreCase)))
                                throw new InvalidOperationException("Declared additional scene missing: " + extra);
                        if (paths.Length != (string.IsNullOrEmpty(entry.scene) ? 0 : 1) + (entry.additionalScenes?.Length ?? 0))
                            throw new InvalidOperationException("Bundle contains undeclared scenes: " + entry.name);
                        if (string.IsNullOrEmpty(entry.scene) && paths.Length != 0)
                            throw new InvalidOperationException("Shared bundle unexpectedly contains scenes.");
                        if (built.GetAssetBundleHash(entry.name).ToString() != entry.hash)
                            throw new InvalidOperationException("Bundle build hash mismatch: " + entry.name);
                        if (!built.GetAllDependencies(entry.name).OrderBy(s => s).SequenceEqual(entry.dependencies.OrderBy(s => s)))
                            throw new InvalidOperationException("Dependency closure mismatch: " + entry.name);
                        long bytes = new FileInfo(Path.Combine(folder, entry.file)).Length;
                        if (bytes != entry.bytes) throw new InvalidOperationException("Bundle length mismatch: " + entry.name);
                        return new BundleAudit { name = entry.name, scenePaths = paths, crcPassed = true, hashMatched = true, bytes = bytes };
                    }
                    finally { bundle.Unload(true); }
                }).ToArray();
                var result = new ReleaseAudit { unityVersion = Application.unityVersion, result = "passed", bundles = rows };
                File.WriteAllText(Path.Combine(folder, "native-validation.json"), JsonUtility.ToJson(result, true));
                Debug.Log("[StreamingReleaseValidator] Passed " + rows.Length + " bundles.");
                return result;
            }
            finally { root.Unload(true); }
        }
    }
}
