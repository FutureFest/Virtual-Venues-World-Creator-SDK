using UnityEditor;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// Shared resolver for the project-local <see cref="NmkrPluginConfig"/>
    /// asset. Used by both the NMKR Settings window and the project-uid
    /// property drawer so there is exactly one implementation of "find the
    /// creator's config".
    /// </summary>
    public static class NmkrConfigLocator
    {
        /// <summary>
        /// Finds the creator's <see cref="NmkrPluginConfig"/>. Prefers an asset
        /// under Assets/VirtualVenuesPluginConfigs/ (the conventional
        /// project-local location); otherwise returns any config that is not
        /// inside the VV SDK package itself. Returns null when none is found.
        /// </summary>
        public static NmkrPluginConfig FindLocalConfig()
        {
            string vvSdkPath = GetVvSdkPackagePath();
            string[] guids = AssetDatabase.FindAssets("t:NmkrPluginConfig");
            NmkrPluginConfig preferred = null;
            NmkrPluginConfig fallback = null;

            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                NmkrPluginConfig cfg = AssetDatabase.LoadAssetAtPath<NmkrPluginConfig>(p);
                if (cfg == null) { continue; }

                if (p.Contains("VirtualVenuesPluginConfigs")) { preferred = cfg; continue; }
                if (!string.IsNullOrEmpty(vvSdkPath) && p.StartsWith(vvSdkPath)) { continue; }
                fallback = cfg;
            }

            return preferred ?? fallback;
        }

        private static string GetVvSdkPackagePath()
        {
            UnityEditor.PackageManager.PackageInfo info =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NmkrConfigLocator).Assembly);
            return info == null ? null : info.assetPath;
        }
    }
}
