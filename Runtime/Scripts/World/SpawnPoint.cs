#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class SpawnPoint : MonoBehaviour
    {
        private static List<SpawnPoint> _instances = new List<SpawnPoint>();

        public static List<SpawnPoint> Instances => _instances;

        public static Action<SpawnPoint> onSpawnPointAdded = null;

#if UNITY_EDITOR
        [Header("Gizmo Settings")]
        [Tooltip("Length of the direction arrow.")]
        private float _arrowLength = 1f;
        [Tooltip("Size of the arrowhead.")]
        private float _arrowHeadSize = 0.50f;
        [Tooltip("Size Center Sphere.")]
        private float _sphereSize = 0.25f;
#endif

        private void Awake()
        {
            AddSpawnPoint(this);
        }

        private static void AddSpawnPoint(SpawnPoint spawnPoint)
        {
            if (_instances.Contains(spawnPoint))
            {
                return;
            }
            _instances.Add(spawnPoint);
            onSpawnPointAdded?.Invoke(spawnPoint);
        }


#if UNITY_EDITOR
        [MenuItem("GameObject/VirtualVenues/Spawn Point", isValidateFunction: false, priority: 0)]
        private static void CreateSpawnPoint(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("SpawnPoint");
            go.AddComponent<SpawnPoint>();

            GameObject parent = menuCommand.context as GameObject;
            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(go, parent);
            }

            Undo.RegisterCreatedObjectUndo(go, "Create Spawn Point");

            Selection.activeObject = go;
        }

        [MenuItem("GameObject/VirtualVenues/Spawn Point", isValidateFunction: true)]
        private static bool ValidateCreateSpawnPoint(MenuCommand menuCommand)
        {
            return true;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;

            Vector3 start = transform.position;
            Vector3 end = start + transform.forward * _arrowLength;

            Gizmos.DrawLine(start, end);

            Quaternion rot1 = Quaternion.LookRotation(transform.forward) * Quaternion.Euler(0, 150f, 0);
            Quaternion rot2 = Quaternion.LookRotation(transform.forward) * Quaternion.Euler(0, -150f, 0);

            Vector3 head1 = end + (rot1 * Vector3.forward) * _arrowHeadSize;
            Vector3 head2 = end + (rot2 * Vector3.forward) * _arrowHeadSize;

            Gizmos.DrawLine(end, head1);
            Gizmos.DrawLine(end, head2);
            Gizmos.DrawSphere(start, _sphereSize);
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(SpawnPoint))]
    public class SpawnPointEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            SpawnPoint sp = (SpawnPoint)target;
            if (GUILayout.Button("Ground to Floor"))
            {
                GroundToFloor(sp);
            }
        }

        private void GroundToFloor(SpawnPoint sp)
        {
            Transform t = sp.transform;
            Vector3 origin = t.position + Vector3.up * 0.1f;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, Mathf.Infinity))
            {
                Undo.RecordObject(t, "Ground Spawn Point");
                t.position = hit.point;
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
            else
            {
                Debug.LogWarning($"[SpawnPointEditor] No collider found below '{t.name}'");
            }
        }
    }
#endif
}

