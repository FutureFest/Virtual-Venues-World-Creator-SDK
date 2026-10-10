using System;
using System.Collections.Generic;

namespace VirtualVenues.WorldCreator
{
    /// <summary>Estimated world working set only: excludes the UPC, catalogs, avatars and browser.</summary>
    public static class StreamingMemoryPolicy
    {
        // Starting engineering ceiling, not an iOS memory limit or proof that a load is safe.
        public const long MobileWorldCeilingBytes = 128L * 1024 * 1024;

        public struct Admission
        {
            public long BudgetBytes;
            public long ResidentBytes;
            public long AddedResidentBytes;
            public long TemporaryBytes;
            public long PeakBytes;
            public bool Allowed => PeakBytes <= BudgetBytes;
        }

        /// <summary>Call only for a validated V2 manifest, after acquiring the mobile load queue.</summary>
        public static Admission Evaluate(WorldStreamingManifest manifest, string sceneBundle,
            IEnumerable<string> residentBundles, IEnumerable<string> activeScenes)
        {
            if (manifest.version != 2) throw new ArgumentException("Memory admission requires a V2 manifest.");
            var bundles = new HashSet<string>(residentBundles, StringComparer.Ordinal);
            var scenes = new HashSet<string>(activeScenes, StringComparer.Ordinal);
            var entry = manifest.FindBundle(sceneBundle);
            if (string.IsNullOrEmpty(entry.scene)) throw new ArgumentException("Memory admission requires a scene bundle.");
            var admission = new Admission { BudgetBytes = Math.Min(manifest.mobileWorldMemoryBudgetBytes, MobileWorldCeilingBytes) };
            checked
            {
                foreach (var name in bundles) admission.ResidentBytes += manifest.FindBundle(name).residentBytes;
                foreach (var name in scenes) admission.ResidentBytes += manifest.FindBundle(name).sceneResidentBytes;
                long loadTemporary = 0;
                // Dependencies are a validated complete closure. Shared leases count once.
                foreach (var name in entry.dependencies)
                    AddBundle(manifest.FindBundle(name), bundles, ref admission, ref loadTemporary);
                AddBundle(entry, bundles, ref admission, ref loadTemporary);
                if (!scenes.Contains(sceneBundle))
                {
                    admission.AddedResidentBytes += entry.sceneResidentBytes;
                    admission.TemporaryBytes = entry.activationTemporaryBytes;
                }
                // Conservatively retain scratch from every newly loaded dependency through
                // activation rather than assuming garbage collection between downloads.
                admission.TemporaryBytes += loadTemporary;
                admission.PeakBytes = admission.ResidentBytes + admission.AddedResidentBytes + admission.TemporaryBytes;
            }
            return admission;
        }

        private static void AddBundle(StreamingBundle entry, HashSet<string> resident,
            ref Admission admission, ref long temporary)
        {
            if (!resident.Add(entry.name)) return;
            admission.AddedResidentBytes = checked(admission.AddedResidentBytes + entry.residentBytes);
            temporary = checked(temporary + entry.loadTemporaryBytes);
        }
    }
}
