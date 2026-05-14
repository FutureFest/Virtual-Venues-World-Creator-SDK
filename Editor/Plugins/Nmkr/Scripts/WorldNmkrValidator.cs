using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// Pre-publish validation for NMKR content in a world scene. NMKR
    /// validation is opt-in by content: a scene with no
    /// <see cref="NmkrMintInteractable"/> always passes.
    /// </summary>
    public static class WorldNmkrValidator
    {
        public class Result
        {
            public bool Ok = true;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
        }

        /// <summary>
        /// Scans <paramref name="sceneAsset"/> for NMKR Mint Interactables. If
        /// any are present, requires a populated <see cref="NmkrPluginConfig"/>
        /// and a non-empty ProjectUid on every interactable. The scene is used
        /// in place if already loaded, otherwise opened additively and closed
        /// again.
        /// </summary>
        public static Result Validate(SceneAsset sceneAsset)
        {
            Result result = new Result();
            if (sceneAsset == null) { return result; }

            string path = AssetDatabase.GetAssetPath(sceneAsset);
            if (string.IsNullOrEmpty(path)) { return result; }

            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedAdditively = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedAdditively = true;
            }

            try
            {
                List<NmkrMintInteractable> interactables = new List<NmkrMintInteractable>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    interactables.AddRange(root.GetComponentsInChildren<NmkrMintInteractable>(true));
                }

                if (interactables.Count == 0) { return result; }

                bool hasSettings = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.GetComponentInChildren<NmkrPluginSettings>(true) != null) { hasSettings = true; break; }
                }
                if (!hasSettings)
                {
                    result.Ok = false;
                    result.Errors.Add(
                        "Scene contains NMKR Mint Interactable(s) but no NMKR_Plugin prefab. " +
                        "Drag the NMKR_Plugin prefab into the scene so the account settings travel with the world.");
                }

                NmkrPluginConfig cfg = NmkrConfigLocator.FindLocalConfig();
                if (cfg == null)
                {
                    result.Ok = false;
                    result.Errors.Add(
                        "Scene contains NMKR Mint Interactable(s) but no NmkrPluginConfig was found. " +
                        "Open VirtualVenues > Plugins > NMKR Settings to create one.");
                }
                else if (string.IsNullOrEmpty(cfg.ApiKey) || cfg.CustomerId == 0)
                {
                    result.Ok = false;
                    result.Errors.Add(
                        "NmkrPluginConfig is incomplete — set the Customer Id and API Key in " +
                        "VirtualVenues > Plugins > NMKR Settings.");
                }

                foreach (NmkrMintInteractable mi in interactables)
                {
                    if (string.IsNullOrEmpty(mi.ProjectUid))
                    {
                        result.Ok = false;
                        result.Errors.Add(
                            $"NMKR Mint Interactable '{GetHierarchyPath(mi.gameObject)}' has no project selected.");
                    }
                }
            }
            finally
            {
                if (openedAdditively) { EditorSceneManager.CloseScene(scene, true); }
            }

            return result;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            string p = go.name;
            Transform t = go.transform.parent;
            while (t != null)
            {
                p = t.name + "/" + p;
                t = t.parent;
            }
            return p;
        }
    }
}
