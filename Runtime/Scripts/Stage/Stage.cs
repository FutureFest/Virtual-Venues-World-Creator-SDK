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

        [Tooltip("When ON, this stage's media (artist/visuals video + screens) is live as soon as the world " +
                 "loads. When OFF, the stage starts dark and is only switched on when a player enters a " +
                 "Stage Trigger zone (a performance optimization for multi-stage venues).")]
        [SerializeField] private bool _activateOnStart = true;

        [Tooltip("Optional name shown in stage pickers and the venue dashboard, e.g. \"Main Stage\".")]
        [SerializeField] private string _displayName = "";

        [Tooltip("Turn a stream's video or audio off in this world. Streams that aren't listed play both.")]
        [SerializeField] private List<StreamSwitch> _streamSwitches = new List<StreamSwitch>();

        [Serializable]
        public class StreamSwitch
        {
            [Min(0)] public int streamIndex = 0;
            public bool video = true;
            public bool audio = true;
        }

        public int StageIndex => _stageIndex;
        public bool ActivateOnStart => _activateOnStart;
        public string DisplayName => _displayName;
        public bool IsStreamVideoOn(int streamIndex) => FindSwitch(streamIndex)?.video ?? true;
        public bool IsStreamAudioOn(int streamIndex) => FindSwitch(streamIndex)?.audio ?? true;

        private StreamSwitch FindSwitch(int streamIndex)
        {
            for (int i = 0; i < _streamSwitches.Count; i++)
            {
                if (_streamSwitches[i] != null && _streamSwitches[i].streamIndex == streamIndex) { return _streamSwitches[i]; }
            }
            return null;
        }

        public static Action<Stage> onStageAdded = null;



        private void Start()
        {
            // Fallback for authored (prefab-placed) stages that are never Configure()'d by the layout:
            // register with the serialized index. Idempotent — a no-op when Configure() already did.
            AddStage(this);
        }

        /// <summary>
        /// Initializes this stage from layout data. Safe to call after Awake/AddComponent.
        /// </summary>
        public void Configure(int stageIndex, bool activateOnStart = true)
        {
            _stageIndex = stageIndex;
            _activateOnStart = activateOnStart;
            // Register AFTER the values are set. The UWE LayoutLoader AddComponents this marker and THEN
            // calls Configure; registering in Awake (as before) made StageCoreConnector and any nested
            // surface read the pre-Configure defaults. AddStage is idempotent, so Start() no-ops here.
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

        private void OnDestroy()
        {
            _instances.Remove(this);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector for the Screen, Speaker and Artist markers. Creators pick a Stage instead of typing its
    /// number: nested markers take the parent Stage, standalone ones take a Stage dragged into the slot or
    /// picked from the scene's Stage markers. The raw _stageIndex stays serialized for layout-configured
    /// markers, which have no Stage reference, and follows the picked Stage via each marker's OnValidate.
    /// </summary>
    public static class StagePickerGUI
    {
        // The SDK editor fills these while the creator is signed in (StageStreamsCatalog), so the pickers
        // show a preview venue's stage and stream names. Signed out they stay null and plain numbers show.
        // Only numbers are ever saved: names live in the venue and can change without touching the world.
        public static Action DrawVenuePicker = null;
        public static Func<KeyValuePair<int, string>[]> VenueStages = null;
        public static Func<int, KeyValuePair<int, string>[]> VenueStreams = null;

        public static void DrawInspector(SerializedObject so)
        {
            so.Update();
            DrawVenuePicker?.Invoke();
            DrawStagePicker(so);
            SerializedProperty it = so.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (it.name == "m_Script" || it.name == "_stage" || it.name == "_stageIndex") { continue; }
                if (it.name == "_streamIndex") { DrawStreamField(it, MarkerStageIndex(so.targetObject)); continue; }
                EditorGUILayout.PropertyField(it, true);
            }
            so.ApplyModifiedProperties();
        }

        private static int MarkerStageIndex(UnityEngine.Object marker)
        {
            if (marker is Screen screen) { return screen.StageIndex; }
            if (marker is Speaker speaker) { return speaker.StageIndex; }
            if (marker is Artist artist) { return artist.StageIndex; }
            return -1;
        }

        /// <summary>"Stage 0 · Main Stage", preferring the world's own name, then the preview venue's.</summary>
        public static string StageLabel(int stageIndex, string worldName)
        {
            string name = worldName;
            if (string.IsNullOrEmpty(name))
            {
                foreach (KeyValuePair<int, string> stage in VenueStages?.Invoke() ?? new KeyValuePair<int, string>[0])
                {
                    if (stage.Key == stageIndex) { name = stage.Value; break; }
                }
            }
            return string.IsNullOrEmpty(name) ? $"Stage {stageIndex}" : $"Stage {stageIndex} · {name}";
        }

        // A dropdown of the preview venue's streams on this stage when there is one, else a number field.
        public static void DrawStreamField(SerializedProperty streamProp, int stageIndex)
        {
            GUIContent label = new GUIContent("Stream", streamProp.tooltip);
            KeyValuePair<int, string>[] streams = stageIndex >= 0 ? VenueStreams?.Invoke(stageIndex) : null;
            if (streams == null || streams.Length == 0 || streamProp.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(streamProp, label);
                return;
            }

            List<int> values = new List<int>();
            List<string> labels = new List<string>();
            foreach (KeyValuePair<int, string> stream in streams)
            {
                values.Add(stream.Key);
                labels.Add(string.IsNullOrEmpty(stream.Value) ? $"Stream {stream.Key}" : $"Stream {stream.Key} · {stream.Value}");
            }
            if (!values.Contains(streamProp.intValue))
            {
                values.Add(streamProp.intValue);
                labels.Add($"Stream {streamProp.intValue} (not set up in this venue)");
            }
            int picked = EditorGUILayout.Popup(label, values.IndexOf(streamProp.intValue), labels.ConvertAll(l => new GUIContent(l)).ToArray());
            streamProp.intValue = values[picked];
        }

        private static void DrawStagePicker(SerializedObject so)
        {
            Component marker = so.targetObject as Component;
            if (marker == null) { return; }

            Stage parent = marker.GetComponentInParent<Stage>(true);
            if (parent != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(new GUIContent("Stage", "Taken from the Stage this object sits under."), StageLabel(parent.StageIndex, parent.DisplayName));
                }
                return;
            }

            SerializedProperty stageProp = so.FindProperty("_stage");
            SerializedProperty indexProp = so.FindProperty("_stageIndex");

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(stageProp, new GUIContent("Stage", "Drag a Stage marker here, or pick one below."));
            if (EditorGUI.EndChangeCheck() && stageProp.objectReferenceValue is Stage dragged)
            {
                indexProp.intValue = dragged.StageIndex;
            }

            List<Stage> stages = FindSceneStages(marker.gameObject.scene);
            string[] labels = new string[stages.Count + 1];
            labels[0] = "Pick a stage";
            int current = 0;
            for (int i = 0; i < stages.Count; i++)
            {
                labels[i + 1] = StageLabel(stages[i].StageIndex, stages[i].DisplayName);
                if (stageProp.objectReferenceValue == stages[i]) { current = i + 1; }
            }

            EditorGUI.showMixedValue = stageProp.hasMultipleDifferentValues;
            int picked = EditorGUILayout.Popup(" ", current, labels);
            EditorGUI.showMixedValue = false;
            if (picked != current && picked > 0)
            {
                stageProp.objectReferenceValue = stages[picked - 1];
                indexProp.intValue = stages[picked - 1].StageIndex;
            }

            if (stageProp.objectReferenceValue == null && !stageProp.hasMultipleDifferentValues)
            {
                string hint = stages.Count == 0
                    ? "Pick a stage. This scene has no Stage marker yet: add one with GameObject > VirtualVenues > New Stage."
                    : "Pick a stage. Without one, this won't show or play any stream.";
                EditorGUILayout.HelpBox(hint, MessageType.Warning);
            }
        }

        // Stages in the marker's own scene (the prefab stage's scene in prefab mode), sorted by number.
        private static List<Stage> FindSceneStages(UnityEngine.SceneManagement.Scene scene)
        {
            List<Stage> stages = new List<Stage>();
            if (!scene.IsValid()) { return stages; }
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                stages.AddRange(root.GetComponentsInChildren<Stage>(true));
            }
            stages.Sort((a, b) => a.StageIndex.CompareTo(b.StageIndex));
            return stages;
        }
    }

    [CustomEditor(typeof(Stage))]
    public class StageEditor : Editor
    {
        private static readonly string[] fixtureOptions = new string[]
        {
            "Moving Head Fixture",
            "PAR Fixture",
            "Strobe Fixture"
        };

        [MenuItem("GameObject/VirtualVenues/New Stage", isValidateFunction: false, priority: 0)]
        private static void CreateStage(MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("Stage", Vector3.zero);
        }

        [MenuItem("GameObject/VirtualVenues/New Stage", isValidateFunction: true)]
        private static bool ValidateCreateStage(MenuCommand menuCommand)
        {
            return true;
        }

        // Signed in with a preview venue, the stage number is picked from the venue's named stages and the
        // venue's streams for it are listed; otherwise the default fields show.
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            StagePickerGUI.DrawVenuePicker?.Invoke();
            KeyValuePair<int, string>[] venueStages = StagePickerGUI.VenueStages?.Invoke();
            SerializedProperty it = serializedObject.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (it.name == "m_Script") { continue; }
                if (it.name == "_stageIndex" && venueStages != null && venueStages.Length > 0 && !it.hasMultipleDifferentValues)
                {
                    DrawVenueStageIndex(it, venueStages);
                    continue;
                }
                EditorGUILayout.PropertyField(it, true);
            }
            serializedObject.ApplyModifiedProperties();

            KeyValuePair<int, string>[] streams = StagePickerGUI.VenueStreams?.Invoke(((Stage)target).StageIndex);
            if (streams != null && streams.Length > 0)
            {
                List<string> names = new List<string>();
                foreach (KeyValuePair<int, string> stream in streams) { names.Add($"Stream {stream.Key} · {stream.Value}"); }
                EditorGUILayout.HelpBox("This stage's streams in the preview venue:\n" + string.Join("\n", names), MessageType.None);
            }
        }

        private static void DrawVenueStageIndex(SerializedProperty indexProp, KeyValuePair<int, string>[] venueStages)
        {
            // The venue only knows the stages of its published world, so a new stage takes "Another number".
            List<int> values = new List<int>();
            List<GUIContent> labels = new List<GUIContent>();
            int highest = -1;
            foreach (KeyValuePair<int, string> stage in venueStages)
            {
                values.Add(stage.Key);
                labels.Add(new GUIContent(string.IsNullOrEmpty(stage.Value) ? $"Stage {stage.Key}" : $"Stage {stage.Key} · {stage.Value}"));
                highest = Mathf.Max(highest, stage.Key);
            }
            labels.Add(new GUIContent("Another number"));
            int other = values.Count;
            int current = values.IndexOf(indexProp.intValue);
            if (current < 0) { current = other; }

            int picked = EditorGUILayout.Popup(new GUIContent("Stage", indexProp.tooltip), current, labels.ToArray());
            if (picked < other) { indexProp.intValue = values[picked]; return; }
            if (current != other) { indexProp.intValue = highest + 1; }
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(indexProp, new GUIContent("Number"));
            EditorGUI.indentLevel--;
        }

        private void OnSceneGUI()
        {
            Stage stage = (Stage)target;
            if (stage == null) return;

            // Draw the spawn fixture button gizmo
            DrawFixtureSpawnButton(stage);
        }

        private void DrawFixtureSpawnButton(Stage stage)
        {
            Vector3 buttonPosition = stage.transform.position + Vector3.up * 2f;
            float buttonSize = HandleUtility.GetHandleSize(buttonPosition) * 0.5f;

            // Check if mouse is near button for highlighting
            float mouseDistance = HandleUtility.DistanceToCircle(buttonPosition, buttonSize);
            bool isHovering = mouseDistance <= 0;

            // Set color based on hover state
            Handles.color = isHovering ? Color.cyan : Color.grey;

            // Draw outer glow when hovering
            if (isHovering)
            {
                Handles.color = new Color(0f, 1f, 1f, 0.3f);
                Handles.DrawWireDisc(buttonPosition, Camera.current.transform.forward, buttonSize * 1.3f);
                Handles.color = Color.cyan;
            }

            if (Handles.Button(buttonPosition, Quaternion.identity, buttonSize, buttonSize, Handles.SphereHandleCap))
            {
                ShowFixtureMenu(stage);
            }

            // Draw Stage title - larger and more prominent
            GUIStyle titleStyle = new GUIStyle();
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.fontSize = 20; // Larger font size

            // Add shadow effect for better visibility
            GUIStyle shadowStyle = new GUIStyle(titleStyle);
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.5f);

            string stageTitle = $"STAGE {stage.StageIndex}";
            Vector3 labelPosition = buttonPosition + Vector3.up * buttonSize * 1.8f;

            // Draw shadow
            Handles.Label(labelPosition + Vector3.right * 0.01f + Vector3.down * 0.01f, stageTitle, shadowStyle);
            // Draw main text
            Handles.Label(labelPosition, stageTitle, titleStyle);

            // Draw "+ Add Fixture" text below button when hovering
            if (isHovering)
            {
                GUIStyle hintStyle = new GUIStyle();
                hintStyle.normal.textColor = Color.cyan;
                hintStyle.alignment = TextAnchor.MiddleCenter;
                hintStyle.fontSize = 12;
                Handles.Label(buttonPosition + Vector3.down * buttonSize * 1.2f, "+ Add Fixture", hintStyle);
            }
        }

        private void ShowFixtureMenu(Stage stage)
        {
            GenericMenu menu = new GenericMenu();

            foreach (string fixtureName in fixtureOptions)
            {
                menu.AddItem(new GUIContent(fixtureName), false, () => SpawnFixture(stage, fixtureName));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Cancel"), false, null);

            menu.ShowAsContext();
        }

        private void SpawnFixture(Stage stage, string fixtureName)
        {
            GameObject prefab = EditorHelpers.LoadFromResources(fixtureName);
            if (prefab == null)
            {
                Debug.LogError($"[StageEditor] Failed to load fixture prefab: {fixtureName}");
                return;
            }

            // Instantiate as prefab to maintain prefab connection
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stage.transform);
            instance.name = fixtureName;

            // Position fixture on stage surface
            Vector3 spawnPosition = stage.transform.position;
            spawnPosition.y += 0.5f; // Slight offset above stage
            instance.transform.position = spawnPosition;

            // Register undo
            Undo.RegisterCreatedObjectUndo(instance, $"Spawn {fixtureName}");

            // Mark scene dirty
            EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);

            // Select the new fixture
            Selection.activeObject = instance;

            Debug.Log($"[StageEditor] Spawned {fixtureName} on stage '{stage.name}'.");
        }
    }
#endif
}
