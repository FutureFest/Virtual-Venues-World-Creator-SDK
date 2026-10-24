using UnityEditor;
using UnityEngine;

namespace VirtualVenues.WorldCreator.Editor
{
    [CustomEditor(typeof(AdminEvent))]
    public class AdminEventEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            AdminEvent adminEvent = (AdminEvent)target;
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox($"Admins fire this in chat with: /event {adminEvent.EventName}", MessageType.None);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                // Same as an offline /event: every marker with this name runs locally.
                if (GUILayout.Button(Application.isPlaying ? "Fire now" : "Fire now (Play mode only)"))
                {
                    AdminEvent.InvokeAll(adminEvent.EventName);
                }
            }
        }
    }
}
