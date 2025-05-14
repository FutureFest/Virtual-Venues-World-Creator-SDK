#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor;
#endif
using UnityEngine;
using System.Collections.Generic;
using System;

namespace VirtualVenues.WorldCreator
{
    public class Stage : MonoBehaviour
    {
        private static List<Stage> _instances = new List<Stage>();
        public static List<Stage> Instances => _instances;

        [SerializeField] private int _stageIndex = 0;

        public int StageIndex => _stageIndex;

        public static Action<Stage> onStageAdded = null;



        private void Awake()
        {
            AddStage(this);
        }

        private static void AddStage(Stage stage)
        {
            if (_instances.Contains(stage))
            {
                return;
            }
            _instances.Add(stage);
            onStageAdded?.Invoke(stage);
        }
    }



#if UNITY_EDITOR
    [CustomEditor(typeof(Stage))]
    public class StageEditor : Editor
    {
        [MenuItem("GameObject/VirtualVenues/New Stage", isValidateFunction: false, priority: 0)]
        private static void CreateStage(MenuCommand menuCommand)
        {
            GameObject prefab = LoadFromResources("Stage");
            if (prefab == null) { return; }
            prefab.name = "New Stage";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(instance, "Spawn Resources NewStage");
            instance.transform.position = Vector3.zero;
            Selection.activeObject = instance;
        }

        [MenuItem("GameObject/VirtualVenues/New Stage", isValidateFunction: true)]
        private static bool ValidateCreateStage(MenuCommand menuCommand)
        {
            return true;
        }

        private static GameObject LoadFromResources(string prefabName)
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
