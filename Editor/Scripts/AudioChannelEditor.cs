using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualVenues.WorldCreator.Editor
{
    [CustomEditor(typeof(AudioChannel))]
    [CanEditMultipleObjects]
    public class AudioChannelEditor : UnityEditor.Editor
    {
        private readonly List<AudioSource> _sources = new List<AudioSource>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty prop = serializedObject.FindProperty("_channel");

            // Choices = the assignable built-ins + the custom channels of every Audio Channel Set in the open scenes.
            AudioChannelSet[] sets = Object.FindObjectsByType<AudioChannelSet>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var ids = new List<string>(AudioChannels.Assignable);
            var labels = new List<string>();
            foreach (string id in ids) { labels.Add($"{AudioChannels.DefaultName(id)} ({id})"); }
            foreach (AudioChannelSet set in sets)
            {
                foreach (CustomAudioChannel c in set.Channels)
                {
                    if (!AudioChannels.IsValidId(c.id) || ids.Contains(c.id) || AudioChannels.IsDefault(AudioChannels.Canonical(c.id))) { continue; }
                    ids.Add(c.id);
                    labels.Add(string.IsNullOrWhiteSpace(c.displayName) ? $"Custom: {c.id}" : $"Custom: {c.displayName} ({c.id})");
                }
            }

            int index = ids.IndexOf(AudioChannels.Canonical(prop.stringValue));  // old ids show as their new channel
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
            int picked = EditorGUILayout.Popup("Channel", index, labels.ToArray());
            EditorGUI.showMixedValue = false;
            if (picked != index && picked >= 0) { prop.stringValue = ids[picked]; }

            if (!prop.hasMultipleDifferentValues && index < 0)
            {
                EditorGUILayout.HelpBox($"'{prop.stringValue}' is not a built-in channel or a custom channel in this scene's Audio Channel Set. The app will use Sound Effects.", MessageType.Warning);
            }

            if (targets.Length == 1)
            {
                ((AudioChannel)target).GetSources(_sources);
                EditorGUILayout.HelpBox(_sources.Count == 0
                    ? "No AudioSource on this object or its children."
                    : $"Routes {_sources.Count} AudioSource(s) here and below.", _sources.Count == 0 ? MessageType.Warning : MessageType.Info);
                DrawCustomChannelButton((AudioChannel)target, sets);
            }
            serializedObject.ApplyModifiedProperties();
        }

        // Custom channels live on the world's one Audio Channel Set: open it, or add it to this scene.
        private static void DrawCustomChannelButton(AudioChannel marker, AudioChannelSet[] sets)
        {
            if (EditorUtility.IsPersistent(marker) || PrefabStageUtility.GetPrefabStage(marker.gameObject) != null) { return; }
            if (sets.Length > 0)
            {
                if (GUILayout.Button("Edit custom channels (Audio Channel Set)")) { Selection.activeObject = sets[0]; EditorGUIUtility.PingObject(sets[0]); }
                return;
            }
            EditorGUILayout.HelpBox("Want your own slider in the players' audio mixer (e.g. Coaster Screams)? Add an Audio Channel Set to the world and list your channels there.", MessageType.None);
            if (!GUILayout.Button("Add Audio Channel Set to this scene")) { return; }
            var go = new GameObject("Audio Channel Set");
            Undo.RegisterCreatedObjectUndo(go, "Add Audio Channel Set");
            Scene scene = marker.gameObject.scene;
            if (scene.IsValid() && go.scene != scene) { SceneManager.MoveGameObjectToScene(go, scene); }
            Undo.AddComponent<AudioChannelSet>(go);
            Selection.activeGameObject = go;
        }
    }

    [CustomEditor(typeof(AudioChannelSet))]
    public class AudioChannelSetEditor : UnityEditor.Editor
    {
        private readonly List<string> _problems = new List<string>();

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox($"Each channel here is its own slider in the players' audio mixer, on top of the built-in ones. Up to {AudioChannels.MaxCustom}. Send sounds to one with an Audio Channel component.", MessageType.None);
            DrawDefaultInspector();
            ((AudioChannelSet)target).Validate(_problems);
            foreach (string p in _problems) { EditorGUILayout.HelpBox(p, MessageType.Warning); }
        }
    }

    /// <summary>One custom channel row: the parent is a dropdown of the built-in channels, not free text.</summary>
    [CustomPropertyDrawer(typeof(CustomAudioChannel))]
    public class CustomAudioChannelDrawer : PropertyDrawer
    {
        private static readonly GUIContent ParentLabel = new GUIContent("Parent", "Built-in channel it sits under. The parent's volume also turns this one down.");

        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            property.isExpanded ? Line * 5 : EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty id = property.FindPropertyRelative(nameof(CustomAudioChannel.id));
            SerializedProperty displayName = property.FindPropertyRelative(nameof(CustomAudioChannel.displayName));
            SerializedProperty parent = property.FindPropertyRelative(nameof(CustomAudioChannel.parent));
            SerializedProperty volume = property.FindPropertyRelative(nameof(CustomAudioChannel.defaultVolume));

            EditorGUI.BeginProperty(position, label, property);
            var r = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string title = !string.IsNullOrWhiteSpace(displayName.stringValue) ? displayName.stringValue
                : !string.IsNullOrEmpty(id.stringValue) ? id.stringValue : label.text;
            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, title, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                r.y += Line; EditorGUI.PropertyField(r, id);
                r.y += Line; EditorGUI.PropertyField(r, displayName);
                r.y += Line; DrawParent(r, parent);
                r.y += Line; EditorGUI.PropertyField(r, volume);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        private static void DrawParent(Rect r, SerializedProperty parent)
        {
            var ids = new List<string>(AudioChannels.Assignable);
            var labels = new List<string>();
            foreach (string id in ids) { labels.Add($"{AudioChannels.DefaultName(id)} ({id})"); }
            int index = ids.IndexOf(AudioChannels.Canonical(parent.stringValue));  // old ids show as their new channel
            if (index < 0) { labels.Add($"'{parent.stringValue}' (not a built-in channel)"); index = labels.Count - 1; }
            int picked = EditorGUI.Popup(r, ParentLabel, index, labels.ConvertAll(l => new GUIContent(l)).ToArray());
            if (picked != index && picked < ids.Count) { parent.stringValue = ids[picked]; }
        }
    }
}
