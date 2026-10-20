using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>Versioned data contract; URLs select this format only through .streaming.json.</summary>
    [Serializable]
    public sealed class WorldStreamingManifest
    {
        public string format = "virtualvenues-streaming-world";
        public int version = 1;
        public string target = "WebGL";
        public string baseBundle;
        public string legacyWorldUrl;
        public int mobileZoneBudget = 1;
        public int desktopZoneBudget = 3;
        // V2: author-supplied decoded working-set estimates, never compressed download size.
        public string memoryEstimateProfile;
        public long mobileWorldMemoryBudgetBytes;
        public StreamingBundle[] bundles = Array.Empty<StreamingBundle>();
        public StreamingZone[] zones = Array.Empty<StreamingZone>();

        public void Validate() => Validate("WebGL");

        public void Validate(string expectedTarget)
        {
            if ((expectedTarget != "WebGL" && expectedTarget != "iOS") || format != "virtualvenues-streaming-world" || (version != 1 && version != 2) || target != expectedTarget)
                throw new ArgumentException("Unsupported streaming world format/version/target.");
            if (version == 2 && (!SafeName(memoryEstimateProfile) || !MemoryCost(mobileWorldMemoryBudgetBytes)))
                throw new ArgumentException("Missing/invalid streaming memory profile or budget.");
            if (bundles == null || bundles.Length == 0 || bundles.Length > 128 || zones == null || zones.Length > 32)
                throw new ArgumentException("Invalid streaming world collection sizes.");
            if (mobileZoneBudget < 1 || desktopZoneBudget < 1 || mobileZoneBudget > 4 || desktopZoneBudget > 32)
                throw new ArgumentException("Invalid streaming zone budgets.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in bundles)
            {
                if (b == null || !SafeName(b.name) || !names.Add(b.name) || b.file != b.name || b.bytes <= 0 ||
                    b.hash == null || b.hash.Length != 32 || !IsHex(b.hash) || b.dependencies == null)
                    throw new ArgumentException("Invalid/duplicate streaming bundle entry.");
                if (version == 2 && (!MemoryCost(b.residentBytes) || !MemoryCost(b.loadTemporaryBytes) ||
                    (string.IsNullOrEmpty(b.scene) ? b.sceneResidentBytes != 0 || b.activationTemporaryBytes != 0 :
                        !MemoryCost(b.sceneResidentBytes) || !MemoryCost(b.activationTemporaryBytes))))
                    throw new ArgumentException("Missing/invalid decoded bundle or scene memory costs.");
            }
            if (!names.Contains(baseBundle)) throw new ArgumentException("Missing base bundle.");
            foreach (var b in bundles)
            {
                var deps = new HashSet<string>();
                foreach (var d in b.dependencies)
                    if (d == b.name || !names.Contains(d) || !deps.Add(d)) throw new ArgumentException("Invalid bundle dependency.");
                // Exporter records the complete transitive closure. No runtime recursive acquisition.
                foreach (var d in b.dependencies)
                    foreach (var indirect in FindBundle(d).dependencies)
                        if (indirect == b.name || !deps.Contains(indirect)) throw new ArgumentException("Incomplete/cyclic dependency closure.");
            }
            var sceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sceneBundles = new HashSet<string>(StringComparer.Ordinal) { baseBundle };
            foreach (var b in bundles)
            {
                if (!string.IsNullOrEmpty(b.scene) && (!b.scene.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !b.scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || !sceneNames.Add(b.scene)))
                    throw new ArgumentException("Invalid/duplicate scene path.");
                if ((b.additionalScenes?.Length ?? 0) > 32 ||
                    ((b.additionalScenes?.Length ?? 0) > 0 && (b.name != baseBundle || version != 1)))
                    throw new ArgumentException("Additional scenes belong to the V1 base world bundle.");
                foreach (string scene in b.additionalScenes ?? Array.Empty<string>())
                    if (string.IsNullOrEmpty(scene) || !scene.StartsWith("Assets/", StringComparison.Ordinal) ||
                        !scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || !sceneNames.Add(scene))
                        throw new ArgumentException("Invalid/duplicate additional scene path.");
                foreach (var d in b.dependencies)
                    if (!string.IsNullOrEmpty(FindBundle(d).scene)) throw new ArgumentException("Scene bundles cannot be dependencies.");
            }
            if (string.IsNullOrEmpty(FindBundle(baseBundle).scene)) throw new ArgumentException("Missing base scene.");
            var ids = new HashSet<string>();
            foreach (var z in zones)
                if (z == null || !SafeName(z.id) || !ids.Add(z.id) || !names.Contains(z.bundle) || !sceneBundles.Add(z.bundle) || string.IsNullOrEmpty(FindBundle(z.bundle).scene) ||
                    !Finite(z.center) || !Finite(z.size) || z.size.x <= 0 || z.size.y <= 0 || z.size.z <= 0 ||
                    !Finite(z.preloadDistance) || !Finite(z.retainDistance) || z.preloadDistance < 1 ||
                    z.retainDistance < z.preloadDistance)
                    throw new ArgumentException("Invalid/duplicate streaming zone.");
            // Room interiors must be disjoint. A one-zone mobile budget cannot serve an overlap.
            for (int a = 0; a < zones.Length; a++)
                for (int b = a + 1; b < zones.Length; b++)
                {
                    var x = zones[a].Bounds; var y = zones[b].Bounds;
                    if (x.min.x < y.max.x && x.max.x > y.min.x && x.min.y < y.max.y && x.max.y > y.min.y &&
                        x.min.z < y.max.z && x.max.z > y.min.z) throw new ArgumentException("Streaming room interiors overlap.");
                }
        }

        public StreamingBundle FindBundle(string name)
        {
            foreach (var b in bundles) if (b.name == name) return b;
            throw new ArgumentException("Unknown bundle: " + name);
        }
        private static bool SafeName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96) return false;
            foreach (char c in value) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            return true;
        }
        private static bool IsHex(string value)
        {
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        // Keeps sums of all 128 entries comfortably below Int64 overflow.
        private static bool MemoryCost(long value) => value > 0 && value <= 1L << 40;
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
    [Serializable]
    public sealed class StreamingBundle
    {
        public string name;
        public string file;
        public string hash;
        public uint crc;
        public long bytes;
        public long residentBytes;
        public long loadTemporaryBytes;
        public long sceneResidentBytes;
        public long activationTemporaryBytes;
        public string scene;
        public string[] additionalScenes = Array.Empty<string>();
        public string[] dependencies = Array.Empty<string>();
    }
    [Serializable]
    public sealed class StreamingZone
    {
        public string id;
        public string label;
        public string bundle;
        public Vector3 center;
        public Vector3 size;
        public float preloadDistance = 8;
        public float retainDistance = 14;
        public bool blockUntilReady = true;
        public Bounds Bounds => new Bounds(center, size);
    }
}
