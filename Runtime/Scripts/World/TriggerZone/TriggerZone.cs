#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Marker component that defines a trigger zone in the world.
    /// Works with any Unity collider component (Box, Sphere, Capsule, Mesh, etc.).
    /// In production, FutureFestWorlds systems connect this to player detection and interaction logic.
    /// Serves as a base for extensible trigger-based features.
    /// </summary>
    public class TriggerZone : MonoBehaviour
    {
        [Header("Zone Settings")]
        [Tooltip("Display name for this trigger zone")]
        [SerializeField] private string _zoneName = "Trigger Zone";

        private static InstanceTracker<TriggerZone> _tracker = new InstanceTracker<TriggerZone>();

        public static InstanceTracker<TriggerZone> Tracker => _tracker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            _tracker = new InstanceTracker<TriggerZone>();
        }

        // Reference to the collider component
        private Collider _collider;
        public Collider Collider => _collider;

        public string ZoneName => _zoneName;

        private void Awake()
        {
            // Find and configure any existing collider
            SetupCollider();

            // Register this zone
            _tracker.AddInstance(this);
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
        }

        /// <summary>
        /// Initializes this trigger zone from layout data. Safe to call after Awake/AddComponent.
        /// Re-runs collider setup so a collider added after Awake is registered as a trigger.
        /// </summary>
        public void Configure(string zoneName)
        {
            _zoneName = zoneName;
            SetupCollider();
        }

        public void SetupCollider()
        {
            // Prefer a collider ALREADY marked isTrigger (the UWE layout loader's collider component, or
            // an authored trigger volume) — never convert a solid collider when a trigger one exists,
            // since the solid one may be the object's deliberate collision shape. Only when NO trigger
            // collider is present fall back to the legacy behaviour: convert the first collider found
            // (creators author "TriggerZone + any collider" and rely on the auto-conversion).
            _collider = null;
            Collider[] colliders = GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].isTrigger) { _collider = colliders[i]; break; }
            }

            if (_collider == null)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null) { _collider = colliders[i]; break; }
                }
            }

            if (_collider != null)
            {
                _collider.isTrigger = true;
            }
        }

#if UNITY_EDITOR
        [MenuItem("GameObject/VirtualVenues/Trigger Zone", isValidateFunction: false, priority: 0)]
        private static void CreateTriggerZone(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("TriggerZone");
            go.AddComponent<TriggerZone>();

            // Add a default BoxCollider for convenience
            BoxCollider boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = new Vector3(5f, 3f, 5f);

            GameObject parent = menuCommand.context as GameObject;
            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(go, parent);
            }

            Undo.RegisterCreatedObjectUndo(go, "Create Trigger Zone");

            Selection.activeObject = go;
        }

        [MenuItem("GameObject/VirtualVenues/Trigger Zone", isValidateFunction: true)]
        private static bool ValidateCreateTriggerZone(MenuCommand menuCommand)
        {
            return true;
        }

        private void OnDrawGizmos()
        {
            // Draw gizmo based on collider type
            Collider col = GetComponent<Collider>();
            if (col == null)
                return;

            // Use semi-transparent yellow for trigger zones
            Color gizmoColor = new Color(1f, 1f, 0f, 0.3f);
            Color wireColor = new Color(1f, 1f, 0f, 0.8f);

            Gizmos.color = gizmoColor;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (col is SphereCollider sphereCol)
            {
                Vector3 center = sphereCol.center;
                float radius = sphereCol.radius;
                Gizmos.color = gizmoColor;
                Gizmos.DrawSphere(center, radius);
                Gizmos.color = wireColor;
                Gizmos.DrawWireSphere(center, radius);
            }
            else if (col is CapsuleCollider capsuleCol)
            {
                Vector3 center = capsuleCol.center;
                float radius = capsuleCol.radius;
                float height = capsuleCol.height;

                Gizmos.color = wireColor;
                Gizmos.DrawWireSphere(center + Vector3.up * (height * 0.5f - radius), radius);
                Gizmos.DrawWireSphere(center + Vector3.down * (height * 0.5f - radius), radius);
            }

            Gizmos.matrix = oldMatrix;

            // Draw zone name label
            DrawZoneLabel();
        }

        private void DrawZoneLabel()
        {
            if (string.IsNullOrEmpty(ZoneName))
                return;

            UnityEditor.Handles.color = Color.yellow;
            Vector3 labelPos = transform.position + Vector3.up * 0.5f;
            UnityEditor.Handles.Label(labelPos, ZoneName, new GUIStyle()
            {
                normal = new GUIStyleState() { textColor = Color.yellow },
                fontSize = 12,
                fontStyle = FontStyle.Bold
            });
        }
#endif
    }
}
