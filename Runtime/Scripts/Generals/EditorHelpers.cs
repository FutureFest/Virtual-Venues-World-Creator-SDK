#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public static class EditorHelpers
    {
        public static void SpawnEditorObject(string objectName, Vector3 spawnPosition)
        {
            GameObject prefab = EditorHelpers.LoadFromResources(objectName);
            if (prefab == null) { return; }
            prefab.name = objectName;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(instance, $"Spawn Resources {objectName}");
            instance.transform.position = spawnPosition;
            Selection.activeObject = instance;
        }


        public static GameObject LoadFromResources(string prefabName)
        {
            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogError($"Resources.Load failed for '{prefabName}'.");
            }
            return prefab;
        }
    }
#endif
}
