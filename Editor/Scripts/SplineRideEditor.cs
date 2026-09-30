using UnityEditor;
using UnityEngine;

namespace VirtualVenues.WorldCreator.Editor
{
    /// <summary>Ride readout in the inspector, and drag handles that slide sections and events along the track.</summary>
    [CustomEditor(typeof(SplineRide))]
    public class SplineRideEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var ride = (SplineRide)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Simulated ride", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(new GUIContent(
                $"Track {ride.Length:0} m   Lap {ride.LapTime:0.0} s   Top speed {ride.TopSpeed:0.0} m/s ({ride.TopSpeed * 3.6f:0} km/h)",
                "Worked out from the track shape, gravity and sections. Track = length, Lap = seconds for one full lap, Top speed = fastest the train goes."));
            foreach (string warning in ride.Warnings) { EditorGUILayout.HelpBox(warning, MessageType.Warning); }
            EditorGUILayout.HelpBox("Drag the coloured handles in the Scene view to move sections and events along the track.", MessageType.None);
            SplineTrackEditor.EditTrackShapeButton();
        }

        private void OnSceneGUI()
        {
            var ride = (SplineRide)target;
            serializedObject.Update();
            SerializedProperty sections = serializedObject.FindProperty("_sections");
            for (int i = 0; i < sections.arraySize; i++)
            {
                SerializedProperty s = sections.GetArrayElementAtIndex(i);
                Handles.color = Color.white;
                Slide(ride, s.FindPropertyRelative("_start"));
                Slide(ride, s.FindPropertyRelative("_end"));
            }
            SerializedProperty events = serializedObject.FindProperty("_events");
            Handles.color = Color.magenta;
            for (int i = 0; i < events.arraySize; i++)
            {
                Slide(ride, events.GetArrayElementAtIndex(i).FindPropertyRelative("_distance"));
            }
            serializedObject.ApplyModifiedProperties();
        }

        // A handle at `metres` along the track; dragging snaps it back onto the track.
        private static void Slide(SplineRide ride, SerializedProperty metres)
        {
            Vector3 p = ride.PositionAt(metres.floatValue);
            float size = HandleUtility.GetHandleSize(p) * 0.08f;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(p, size, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck()) { metres.floatValue = Mathf.Round(ride.NearestMetres(moved) * 10f) / 10f; }
        }
    }

    /// <summary>Labels each section in the list by its type and range ("Lift (22–55 m)") instead of "Element 0".</summary>
    [CustomPropertyDrawer(typeof(TrackSection))]
    public class TrackSectionDrawer : PropertyDrawer
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
            SerializedProperty type = property.FindPropertyRelative("_type");
            string name = type.enumValueIndex >= 0 && type.enumValueIndex < type.enumDisplayNames.Length ? type.enumDisplayNames[type.enumValueIndex] : label.text;
            var title = new GUIContent($"{name} ({property.FindPropertyRelative("_start").floatValue:0}–{property.FindPropertyRelative("_end").floatValue:0} m)",
                $"{name} section, from Start to End metres along the track. {Describe(type.enumValueIndex)}");

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

        private static string Describe(int type)
        {
            switch ((TrackSectionType)type)
            {
                case TrackSectionType.Lift: return "A chain pulls the train uphill at Speed.";
                case TrackSectionType.Booster: return "Magnets push the train up to Speed.";
                case TrackSectionType.Brake: return "Slows the train down to Speed.";
                case TrackSectionType.Station: return "Stops the train at Start, waits Hold Seconds, then launches it to Speed.";
                default: return string.Empty;
            }
        }
    }
}
