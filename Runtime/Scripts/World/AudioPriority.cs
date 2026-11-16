using System;
using System.Globalization;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Audio priority (ducking), shared by <see cref="AudioChannel"/>, <see cref="Speaker"/> and
    /// <see cref="AudioOcclusionZone"/>. Everything defaults to priority 0, where nothing changes: all overlapping
    /// audio plays as before. A source with a higher priority turns down every lower-priority source the closer the
    /// listener is to it, while it is actually playing, down to each lower source's own duck floor. An occlusion
    /// zone does the same for sources outside it while the listener is inside it. The VirtualVenues app does the
    /// mixing at runtime, as one more gain on top of the channel volumes and the distance fade; nothing changes in
    /// the editor.
    /// </summary>
    public static class AudioPriority
    {
        public const int DefaultPriority = 0;
        public const float DefaultDuckFloor = 0.05f;
        public const int DefaultZonePriority = 1;
        public const float DefaultEdgeBlend = 2f;

        public const string PriorityLabel = "Priority";
        public const string DuckFloorLabel = "Duck floor";
        public const string HelpText = "0 = no priority. A higher number turns down lower-priority audio as the listener gets closer.";
        public const string PriorityTooltip = "0 = no priority. While this plays, audio with a lower priority is turned down, more the closer the listener is to this.";
        public const string DuckFloorTooltip = "How far this is turned down when higher-priority audio (or an occlusion zone) is at full strength. 5% = nearly silent, 100% = never turned down.";

        /// <summary>Raised by the SDK components when a priority, floor or zone setting changes (code or Inspector).</summary>
        public static Action<Component> onPriorityChanged = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() { onPriorityChanged = null; }

        public static float ClampFloor(float floor) => float.IsNaN(floor) ? DefaultDuckFloor : Mathf.Clamp01(floor);

        /// <summary>
        /// Layout JSON stores the floor as a whole percent string; "" (or a missing key) is the default 5%, so
        /// layouts saved before this field existed read the default. Anything unreadable also reads the default.
        /// </summary>
        public static float ParseFloor(string percent)
        {
            if (string.IsNullOrEmpty(percent)) { return DefaultDuckFloor; }
            return float.TryParse(percent, NumberStyles.Float, CultureInfo.InvariantCulture, out float p)
                ? Mathf.Clamp01(p / 100f) : DefaultDuckFloor;
        }

        /// <summary>The layout string for a floor: "" for the default, else a whole percent ("0".."100").</summary>
        public static string FormatFloor(float floor)
        {
            int percent = Mathf.RoundToInt(ClampFloor(floor) * 100f);
            return percent == Mathf.RoundToInt(DefaultDuckFloor * 100f) ? string.Empty : percent.ToString(CultureInfo.InvariantCulture);
        }
    }

#if UNITY_EDITOR
    /// <summary>The "Priority" + "Duck floor" Inspector block used by AudioChannel and Speaker.</summary>
    public static class AudioPriorityGUI
    {
        public const string PriorityField = "_priority";
        public const string DuckFloorField = "_duckFloor";

        /// <summary>Draws both fields of <paramref name="so"/>. Call between Update and ApplyModifiedProperties.</summary>
        public static void Draw(UnityEditor.SerializedObject so)
        {
            UnityEditor.SerializedProperty priority = so.FindProperty(PriorityField);
            UnityEditor.SerializedProperty floor = so.FindProperty(DuckFloorField);
            if (priority == null || floor == null) { return; }

            UnityEditor.EditorGUILayout.Space(4);
            UnityEditor.EditorGUILayout.LabelField("Audio priority", UnityEditor.EditorStyles.boldLabel);
            UnityEditor.EditorGUILayout.PropertyField(priority, new GUIContent(AudioPriority.PriorityLabel, AudioPriority.PriorityTooltip));

            UnityEditor.EditorGUI.showMixedValue = floor.hasMultipleDifferentValues;
            UnityEditor.EditorGUI.BeginChangeCheck();
            int percent = UnityEditor.EditorGUILayout.IntSlider(new GUIContent(AudioPriority.DuckFloorLabel + " (%)", AudioPriority.DuckFloorTooltip),
                Mathf.RoundToInt(floor.floatValue * 100f), 0, 100);
            if (UnityEditor.EditorGUI.EndChangeCheck()) { floor.floatValue = percent / 100f; }
            UnityEditor.EditorGUI.showMixedValue = false;

            UnityEditor.EditorGUILayout.HelpBox(AudioPriority.HelpText, UnityEditor.MessageType.None);
        }
    }
#endif
}
