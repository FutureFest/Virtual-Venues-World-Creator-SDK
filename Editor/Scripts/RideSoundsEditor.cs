using UnityEditor;
using UnityEngine;

namespace VirtualVenues.WorldCreator.Editor
{
    /// <summary>Live readout of the cart's speed, section and each speed layer's volume and pitch in Play mode.</summary>
    [CustomEditor(typeof(RideSounds))]
    public class RideSoundsEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var sounds = (RideSounds)target;
            SplineMover mover = sounds.Mover;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
            if (!Application.isPlaying || mover == null)
            {
                EditorGUILayout.HelpBox("Enter Play mode to see the cart's speed, section and each layer's volume and pitch.", MessageType.None);
                return;
            }

            TrackSection section = mover.Section;
            EditorGUILayout.LabelField(new GUIContent(
                $"Speed {mover.Speed:0.0} m/s ({mover.Speed * 3.6f:0} km/h)   Section {(section != null ? $"{section.Type} ({section.Start:0}–{section.End:0} m)" : "none")}",
                "From the SplineMover on this object, this frame."));
            for (int i = 0; i < sounds.SpeedLayers.Count; i++)
            {
                RideSpeedLayer layer = sounds.SpeedLayers[i];
                layer.Evaluate(mover.Speed, out float volume, out float pitch);
                string name = layer.Source != null ? layer.Source.name : "(no source)";
                EditorGUILayout.LabelField($"  {name}", $"volume {volume:0.00}   pitch {pitch:0.00}");
            }
            if (sounds.Lift != null) { EditorGUILayout.LabelField("  Lift", sounds.Lift.isPlaying ? "playing" : "off"); }
        }
    }

    /// <summary>Labels each speed layer by its source and band ("Wheels (4–25 m/s)") instead of "Element 0".</summary>
    [CustomPropertyDrawer(typeof(RideSpeedLayer))]
    public class RideSpeedLayerDrawer : PropertyDrawer
    {
        // Children are drawn one by one: PropertyField on this property would re-enter this drawer.
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) { return height; }
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            for (bool more = child.NextVisible(true); more && !SerializedProperty.EqualContents(child, end); more = child.NextVisible(false))
            {
                height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Object source = property.FindPropertyRelative("_source").objectReferenceValue;
            var title = new GUIContent(
                $"{(source != null ? source.name : "Layer")} ({property.FindPropertyRelative("_minSpeed").floatValue:0}–{property.FindPropertyRelative("_maxSpeed").floatValue:0} m/s)",
                "A loop heard between Min and Max Speed, fading over Fade at each edge, its pitch going from Pitch At Min to Pitch At Max.");

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, title, true);
            if (!property.isExpanded) { return; }

            EditorGUI.indentLevel++;
            float y = line.yMax + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            for (bool more = child.NextVisible(true); more && !SerializedProperty.EqualContents(child, end); more = child.NextVisible(false))
            {
                float h = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), child, true);
                y += h + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.indentLevel--;
        }
    }
}
