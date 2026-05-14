using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// At bundle-build time, injects the creator's <see cref="NmkrPluginConfig"/>
    /// values into every <see cref="NmkrPluginSettings"/> component in the scene
    /// being built. The full set (including the apiKey) goes into the UMS /
    /// dedicated-server bundle; the apiKey is omitted from every other target —
    /// notably the WebGL UPC bundle that players download — so the key never
    /// reaches an untrusted client.
    /// </summary>
    /// <remarks>
    /// Unity runs this on a throwaway copy of the scene during the build, so the
    /// creator's on-disk scene is never modified.
    /// </remarks>
    public class NmkrBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            List<NmkrPluginSettings> targets = FindSettings(scene);
            if (targets.Count == 0) { return; }

            // Only the dedicated-server (UMS) build may carry the apiKey.
            bool includeApiKey = EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64;
            NmkrPluginConfig config = NmkrConfigLocator.FindLocalConfig();

            foreach (NmkrPluginSettings target in targets)
            {
                Inject(target, config, includeApiKey);
            }
        }

        private static List<NmkrPluginSettings> FindSettings(Scene scene)
        {
            List<NmkrPluginSettings> found = new List<NmkrPluginSettings>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<NmkrPluginSettings>(true));
            }
            return found;
        }

        private static void Inject(NmkrPluginSettings target, NmkrPluginConfig config, bool includeApiKey)
        {
            SerializedObject so = new SerializedObject(target);

            if (config == null)
            {
                so.FindProperty("_apiKey").stringValue = string.Empty;
                so.ApplyModifiedProperties();
                Debug.LogWarning(
                    $"[NmkrBuildProcessor] No NmkrPluginConfig found — '{target.gameObject.name}' left without NMKR settings.");
                return;
            }

            so.FindProperty("_customerId").intValue = config.CustomerId;
            so.FindProperty("_environment").enumValueIndex = (int)config.Environment;
            so.FindProperty("_payoutWalletAddress").stringValue = config.PayoutWalletAddress ?? string.Empty;
            so.FindProperty("_displayName").stringValue = config.DisplayName ?? string.Empty;
            so.FindProperty("_apiKey").stringValue = includeApiKey ? (config.ApiKey ?? string.Empty) : string.Empty;
            so.ApplyModifiedProperties();

            Debug.Log(
                $"[NmkrBuildProcessor] Injected NMKR settings into '{target.gameObject.name}' " +
                $"(target={EditorUserBuildSettings.activeBuildTarget}, apiKey={(includeApiKey ? "included" : "omitted")}).");
        }
    }
}
