using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// Hierarchy right-click + GameObject menu entries that drop the creator's
    /// NMKR pieces into the scene without them having to hunt for the prefabs
    /// under VirtualVenuesSDK/Runtime/Resources/.
    /// </summary>
    public static class NmkrCreateMenu
    {
        private const string PLUGIN_PREFAB_NAME = "NMKR_Plugin";
        private const string MINT_INTERACTABLE_PREFAB_NAME = "NmkrMintInteractable";

        private const string ADD_PLUGIN_MENU = "GameObject/VirtualVenues/Plugins/NMKR/Add NMKR Plugin";
        private const string ADD_MINT_INTERACTABLE_MENU = "GameObject/VirtualVenues/Plugins/NMKR/Add Mint Interactable";

        [MenuItem(ADD_PLUGIN_MENU, isValidateFunction: false, priority: 0)]
        private static void AddNmkrPlugin(MenuCommand menuCommand)
        {
            // Only one settings carrier is meaningful per scene — NmkrBuildProcessor
            // injects into every NmkrPluginSettings it finds, so a duplicate just
            // means the same credentials get stamped twice. If one already exists,
            // surface it instead of spawning another.
            NmkrPluginSettings existing = FindExistingPluginSettings();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.Log($"[NMKR] '{existing.gameObject.name}' is already in the scene.", existing.gameObject);
                return;
            }

            SpawnPrefab(PLUGIN_PREFAB_NAME, parent: null);
        }

        [MenuItem(ADD_PLUGIN_MENU, isValidateFunction: true)]
        private static bool ValidateAddNmkrPlugin(MenuCommand menuCommand)
        {
            return true;
        }

        [MenuItem(ADD_MINT_INTERACTABLE_MENU, isValidateFunction: false, priority: 0)]
        private static void AddNmkrMintInteractable(MenuCommand menuCommand)
        {
            SpawnPrefab(MINT_INTERACTABLE_PREFAB_NAME, parent: menuCommand.context as GameObject);
        }

        [MenuItem(ADD_MINT_INTERACTABLE_MENU, isValidateFunction: true)]
        private static bool ValidateAddNmkrMintInteractable(MenuCommand menuCommand)
        {
            return true;
        }

        private static void SpawnPrefab(string resourceName, GameObject parent)
        {
            GameObject prefab = Resources.Load<GameObject>(resourceName);
            if (prefab == null)
            {
                Debug.LogError($"[NMKR] Could not load Resources/{resourceName}. Make sure the prefab is under a Resources/ folder in the VV SDK.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(instance, parent);
            }

            Undo.RegisterCreatedObjectUndo(instance, $"Create {instance.name}");
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
        }

        private static NmkrPluginSettings FindExistingPluginSettings()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) { continue; }
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    NmkrPluginSettings found = root.GetComponentInChildren<NmkrPluginSettings>(true);
                    if (found != null) { return found; }
                }
            }
            return null;
        }
    }
}
