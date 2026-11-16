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

#if UNITY_EDITOR
        // A Stage added in the editor takes the lowest stage number no other Stage in its scene uses.
        private void Reset()
        {
            _stageIndex = StagePickerGUI.LowestFreeIndex(gameObject.scene, this);
        }
#endif

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
            DrawScreenAndArtistWarning(so);
            DrawStagePicker(so);
            SerializedProperty it = so.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (it.name == "m_Script" || it.name == "_stage" || it.name == "_stageIndex") { continue; }
                if (it.name == "_streamIndex") { DrawStreamField(it, MarkerStageIndex(so.targetObject)); continue; }
                if (it.name == "_streamLayout") { DrawStreamLayoutField(it, so); continue; }
                if (it.name == "_content") { DrawScreenContentField(it, so); continue; }
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

        private const string WholeLine = "Shows the whole video frame: a movie, or a full-frame artist/visuals stream.";
        private const string StackedArtistLine = "Shows the top half of a stacked video (artist on top, visuals below). One stream carries both, in perfect sync.";
        private const string StackedScreenLine = "Shows the bottom half of a stacked video (artist on top, visuals below). One stream carries both, in perfect sync.";
        private const string CustomMaterialLine = " The mesh has its own material, so it was left as it is: its own tiling and offset are used when the world runs.";
        private const string MixedLayoutLine = "The selected objects show different parts of the video.";
        // Whole first: it is what a newly placed Screen or Artist starts with.
        private static readonly GUIContent[] StreamLayoutLabels = { new GUIContent("Whole video"), new GUIContent("Stacked") };
        private static readonly int[] StreamLayoutValues = { (int)StreamLayout.Whole, (int)StreamLayout.Stacked };

        // The Screen/Artist "Stream Layout" popup, then one line on what the surface will show. Picking a stream layout also swaps
        // the mesh's SDK stage material for the new stream layout's on every selected marker (StageStreamLayoutAssets), in one undo
        // step; the world then uses that material's tiling/offset and picture. The swap writes through its own SerializedObjects; this one picks the change up next repaint.
        private static void DrawStreamLayoutField(SerializedProperty layoutProp, SerializedObject so)
        {
            EditorGUI.showMixedValue = layoutProp.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.IntPopup(new GUIContent("Stream Layout", layoutProp.tooltip), layoutProp.intValue, StreamLayoutLabels, StreamLayoutValues);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (changed)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Change Stream Layout");
                int group = Undo.GetCurrentGroup();
                foreach (UnityEngine.Object target in so.targetObjects) { StageStreamLayoutAssets.SetStreamLayout(target, (StreamLayout)picked); }
                Undo.CollapseUndoOperations(group);
            }
            if (layoutProp.hasMultipleDifferentValues) { EditorGUILayout.HelpBox(MixedLayoutLine, MessageType.None); return; }

            string line = null;
            foreach (UnityEngine.Object target in so.targetObjects)
            {
                string targetLine = StreamLayoutHelpLine(target, (StreamLayout)layoutProp.intValue);
                if (line != null && line != targetLine) { line = MixedLayoutLine; break; }
                line = targetLine;
            }
            EditorGUILayout.HelpBox(line, MessageType.None);
        }

        private static string StreamLayoutHelpLine(UnityEngine.Object marker, StreamLayout streamLayout)
        {
            string line = streamLayout == StreamLayout.Whole ? WholeLine : ShowsArtist(marker) ? StackedArtistLine : StackedScreenLine;
            return StageStreamLayoutAssets.HasCustomMaterial(marker) ? line + CustomMaterialLine : line;
        }

        // An Artist, or a Screen set to Shows: Artist. Stacked, both show the top half.
        private static bool ShowsArtist(UnityEngine.Object marker)
        {
            return marker is Artist || (marker is Screen screen && screen.Content == ScreenContent.Artist);
        }

        private const string VisualsContentLine = "Shows the video as it is: visuals, a movie, slides.";
        private const string ArtistContentLine = "Keys out the green screen, like the Artist cut-out, but stays flat. Put it in front of a visuals Screen.";
        private const string CustomMaterialContentLine = " A mesh with its own material was left as it is.";
        private const string MixedContentLine = "The selected screens show different things.";
        private static readonly GUIContent[] ScreenContentLabels = { new GUIContent("Visuals"), new GUIContent("Artist (green screen)") };
        private static readonly int[] ScreenContentValues = { (int)ScreenContent.Visuals, (int)ScreenContent.Artist };

        // The Screen "Shows" popup, then one line on what it does. Picking also swaps the mesh's SDK stage material on every
        // selected screen (StageStreamLayoutAssets.SetScreenContent: the Artist's chroma key pair for Artist), in one undo step.
        private static void DrawScreenContentField(SerializedProperty contentProp, SerializedObject so)
        {
            EditorGUI.showMixedValue = contentProp.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.IntPopup(new GUIContent("Shows", contentProp.tooltip), contentProp.intValue, ScreenContentLabels, ScreenContentValues);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (changed)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Change Screen Shows");
                int group = Undo.GetCurrentGroup();
                foreach (UnityEngine.Object target in so.targetObjects) { StageStreamLayoutAssets.SetScreenContent(target, (ScreenContent)picked); }
                Undo.CollapseUndoOperations(group);
            }
            if (contentProp.hasMultipleDifferentValues) { EditorGUILayout.HelpBox(MixedContentLine, MessageType.None); return; }

            string line = contentProp.intValue == (int)ScreenContent.Artist ? ArtistContentLine : VisualsContentLine;
            foreach (UnityEngine.Object target in so.targetObjects)
            {
                if (StageStreamLayoutAssets.HasCustomMaterial(target)) { line += CustomMaterialContentLine; break; }
            }
            EditorGUILayout.HelpBox(line, MessageType.None);
        }

        private const string ScreenAndArtistLine = "This object has both a Screen and an Artist. They drive the same mesh and fight over it. " +
            "For a flat artist layer with the green screen keyed out, use a Screen set to Shows: Artist (green screen) and remove the Artist component.";

        /// <summary>True for a Screen or an Artist whose GameObject has both components.</summary>
        public static bool HasScreenAndArtist(UnityEngine.Object marker)
        {
            if (!(marker is Screen) && !(marker is Artist)) { return false; }
            Component component = (Component)marker;
            return component.GetComponent<Screen>() != null && component.GetComponent<Artist>() != null;
        }

        private static void DrawScreenAndArtistWarning(SerializedObject so)
        {
            foreach (UnityEngine.Object target in so.targetObjects)
            {
                if (!HasScreenAndArtist(target)) { continue; }
                EditorGUILayout.HelpBox(ScreenAndArtistLine, MessageType.Warning);
                return;
            }
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
            EditorGUILayout.PropertyField(stageProp, new GUIContent("Stage", "Drag a Stage marker here, or pick one with the circle button."));
            if (EditorGUI.EndChangeCheck() && stageProp.objectReferenceValue is Stage dragged)
            {
                indexProp.intValue = dragged.StageIndex;
            }

            if (stageProp.objectReferenceValue == null && !stageProp.hasMultipleDifferentValues)
            {
                string hint = FindSceneStages(marker.gameObject.scene).Count == 0
                    ? "Pick a stage. This scene has no Stage marker yet: add one with GameObject > VirtualVenues > Stage > Stage."
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

        /// <summary>The lowest stage number no other Stage in the scene uses (0, 1, 2... in creation order).</summary>
        public static int LowestFreeIndex(UnityEngine.SceneManagement.Scene scene, Stage except)
        {
            HashSet<int> used = new HashSet<int>();
            foreach (Stage stage in FindSceneStages(scene)) { if (stage != except) { used.Add(stage.StageIndex); } }
            int index = 0;
            while (used.Contains(index)) { index++; }
            return index;
        }

        /// <summary>Another Stage in the same scene with this Stage's number, or null.</summary>
        public static Stage OtherStageWithIndex(Stage stage)
        {
            foreach (Stage other in FindSceneStages(stage.gameObject.scene))
            {
                if (other != stage && other.StageIndex == stage.StageIndex) { return other; }
            }
            return null;
        }
    }

    // A Stage dragged in, duplicated, pasted or spawned from the menu takes the next free number when its own
    // number is already taken, so new stages come out 0, 1, 2... The creator can still change it afterwards.
    [InitializeOnLoad]
    internal static class StageAutoNumber
    {
        static StageAutoNumber()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { return; } // runtime stages number themselves
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy) { continue; }
                stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs created);
#if UNITY_6000_3_OR_NEWER
                GameObject root = EditorUtility.EntityIdToObject(created.entityId) as GameObject;
#else
                GameObject root = EditorUtility.InstanceIDToObject(created.instanceId) as GameObject;
#endif
                if (root == null) { continue; }
                foreach (Stage stage in root.GetComponentsInChildren<Stage>(true))
                {
                    if (StagePickerGUI.OtherStageWithIndex(stage) == null) { continue; }
                    SerializedObject so = new SerializedObject(stage);
                    so.FindProperty("_stageIndex").intValue = StagePickerGUI.LowestFreeIndex(stage.gameObject.scene, stage);
                    so.ApplyModifiedProperties();
                }
            }
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

        [MenuItem(CreateMenu.StageMenu + "Stage", isValidateFunction: false, priority: CreateMenu.StageOrder)]
        private static void CreateStage(MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("Stage", Vector3.zero);
        }

        [MenuItem(CreateMenu.StageMenu + "Stage", isValidateFunction: true)]
        private static bool ValidateCreateStage(MenuCommand menuCommand)
        {
            return true;
        }

        // The stage number is a plain field. A clash with another Stage is allowed but warned about; signed in
        // with a preview venue, the venue's streams for this stage are listed underneath.
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            StagePickerGUI.DrawVenuePicker?.Invoke();
            SerializedProperty it = serializedObject.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (it.name == "m_Script") { continue; }
                EditorGUILayout.PropertyField(it, true);
                if (it.name == "_stageIndex" && !it.hasMultipleDifferentValues) { DrawIndexClashWarning(); }
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

        private void DrawIndexClashWarning()
        {
            Stage stage = (Stage)target;
            Stage other = StagePickerGUI.OtherStageWithIndex(stage);
            if (other == null) { return; }
            int free = StagePickerGUI.LowestFreeIndex(stage.gameObject.scene, stage);
            EditorGUILayout.HelpBox($"'{other.name}' already uses stage {stage.StageIndex}. Two stages with the same number share " +
                $"one set of streams and lights. Stage {free} is free.", MessageType.Warning);
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
