using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

            // Choices = the assignable defaults + the custom ids of every Audio Channel Set in the open scenes.
            var ids = new List<string>(AudioChannels.Assignable);
            foreach (AudioChannelSet set in Object.FindObjectsByType<AudioChannelSet>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (CustomAudioChannel c in set.Channels)
                {
                    if (AudioChannels.IsValidId(c.id) && !ids.Contains(c.id)) { ids.Add(c.id); }
                }
            }
            var labels = new List<string>();
            foreach (string id in ids) { labels.Add(AudioChannels.IsDefault(id) ? $"{AudioChannels.DefaultName(id)} ({id})" : $"Custom: {id}"); }

            int index = ids.IndexOf(prop.stringValue);
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
            int picked = EditorGUILayout.Popup("Channel", index, labels.ToArray());
            EditorGUI.showMixedValue = false;
            if (picked != index && picked >= 0) { prop.stringValue = ids[picked]; }

            if (!prop.hasMultipleDifferentValues && index < 0)
            {
                EditorGUILayout.HelpBox($"'{prop.stringValue}' is not a default channel or a custom channel in this scene's Audio Channel Set. The app will use Sound Effects.", MessageType.Warning);
            }

            if (targets.Length == 1)
            {
                ((AudioChannel)target).GetSources(_sources);
                EditorGUILayout.HelpBox(_sources.Count == 0
                    ? "No AudioSource on this object or its children."
                    : $"Routes {_sources.Count} AudioSource(s) here and below.", _sources.Count == 0 ? MessageType.Warning : MessageType.Info);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(AudioChannelSet))]
    public class AudioChannelSetEditor : UnityEditor.Editor
    {
        private readonly List<string> _problems = new List<string>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox($"Parent must be one of: {string.Join(", ", AudioChannels.Assignable)}. Up to {AudioChannels.MaxCustom} channels.", MessageType.None);
            ((AudioChannelSet)target).Validate(_problems);
            foreach (string p in _problems) { EditorGUILayout.HelpBox(p, MessageType.Warning); }
        }
    }
}
