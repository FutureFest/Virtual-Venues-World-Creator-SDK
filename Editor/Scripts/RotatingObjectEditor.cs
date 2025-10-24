using UnityEditor;
using UnityEngine;

namespace VirtualVenues.WorldCreator.Editor
{
    [CustomEditor(typeof(RotatingObject))]
    public class RotatingObjectEditor : UnityEditor.Editor
    {
        private SerializedProperty rotationAxisProp;
        private SerializedProperty networkSyncedProp;

        private void OnEnable()
        {
            rotationAxisProp = serializedObject.FindProperty("rotationAxis");
            networkSyncedProp = serializedObject.FindProperty("networkSynced");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            RotatingObject rotatingObject = (RotatingObject)target;

            // Header
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Rotating Object Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This component provides runtime rotation preview when used standalone.\n" +
                "In production: This component is replaced by NetworkedRotation for proper synchronization.",
                MessageType.Info
            );
            EditorGUILayout.Space(5);

            // Rotation Settings
            EditorGUILayout.LabelField("Rotation Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(rotationAxisProp, new GUIContent("Rotation Axis (deg/sec)",
                "Rotation speed per axis in degrees per second. Use X, Y, Z to control rotation around each axis."));

            // Show rotation speed magnitude
            float magnitude = rotationAxisProp.vector3Value.magnitude;
            if (magnitude > 0.01f)
            {
                EditorGUILayout.LabelField($"Total Rotation Speed: {magnitude:F1} deg/sec", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Rotation axis is zero. Object will not rotate.", MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            // Network Settings
            EditorGUILayout.LabelField("Network Settings", EditorStyles.boldLabel);

            // Color-coded network sync checkbox
            Color originalColor = GUI.backgroundColor;
            if (networkSyncedProp.boolValue)
            {
                GUI.backgroundColor = new Color(0.5f, 1f, 0.5f); // Light green
            }

            EditorGUILayout.PropertyField(networkSyncedProp, new GUIContent("Network Synced",
                "When enabled, rotation will be synchronized across all clients using deterministic time. No network bandwidth required!"));

            GUI.backgroundColor = originalColor;

            if (networkSyncedProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "✓ Network Synced - Rotation will be identical on all clients using Mirror.NetworkTime (zero bandwidth).",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Local Only - Rotation will only be visible to this client.",
                    MessageType.None
                );
            }

            EditorGUILayout.Space(10);

            // Runtime Behavior Information
            EditorGUILayout.LabelField("Runtime Behavior", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "• Standalone/Preview: This component rotates the object in Play mode\n" +
                "• Production: NetworkedRotation replaces this component at runtime\n" +
                "• This allows preview in WorldCreatorSDK without requiring core systems",
                MessageType.None
            );

            EditorGUILayout.Space(10);

            // Preview button (editor-only rotation preview)
            EditorGUILayout.LabelField("Editor Preview", EditorStyles.boldLabel);
            if (GUILayout.Button("Preview Rotation in Scene View"))
            {
                rotatingObject.transform.Rotate(rotatingObject.rotationAxis * Time.deltaTime, Space.Self);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(5);

            // Gizmo information
            EditorGUILayout.LabelField("Gizmo Colors:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  Red = X-axis rotation", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  Green = Y-axis rotation", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  Blue = Z-axis rotation", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  Cyan circle = Network synced", EditorStyles.miniLabel);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
