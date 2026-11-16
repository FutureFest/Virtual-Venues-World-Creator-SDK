using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace VirtualVenues.WorldCreator
{
    [Obsolete("Screens, speakers and artists pick a stream by number (StreamIndex). Artist is stream 0, Visuals is stream 1.")]
    public enum SpeakerSource
    {
        Artist,
        Visuals
    }

    /// <summary>
    /// A stage's streams are numbered 0, 1, 2... Layout data stores the number as a string: "Artist" for 0
    /// and "Visuals" for 1 (UPC builds that only know those two names keep working), digits from 2 up.
    /// </summary>
    public static class StageStreams
    {
        public static int Parse(string value)
        {
            if (value == "Visuals") { return 1; }
            return int.TryParse(value, out int index) && index > 0 ? index : 0;
        }

        public static string Format(int streamIndex)
        {
            if (streamIndex == 1) { return "Visuals"; }
            return streamIndex > 1 ? streamIndex.ToString() : "Artist";
        }
    }

    public enum SpeakerChannel
    {
        Mono,
        StereoLeft,
        StereoRight
    }

    [RequireComponent(typeof(AudioSource))]
    public class Speaker : MonoBehaviour
    {
        private static List<Speaker> _instances = new List<Speaker>();

        [SerializeField] private Stage _stage = null;
        [SerializeField] private int _stageIndex = 0;
        [Space]
        [Tooltip("Which of the stage's streams this speaker plays. Stream 0 used to be called Artist, stream 1 Visuals.")]
        [FormerlySerializedAs("_source")]
        [SerializeField, Min(0)] private int _streamIndex = 0;
        [SerializeField] private SpeakerChannel _channel = SpeakerChannel.Mono;
        [Tooltip(AudioPriority.PriorityTooltip)]
        [SerializeField] private int _priority = AudioPriority.DefaultPriority;
        [Tooltip(AudioPriority.DuckFloorTooltip)]
        [SerializeField, Range(0f, 1f)] private float _duckFloor = AudioPriority.DefaultDuckFloor;

        private AudioSource _audioSource = null;

        public static List<Speaker> Instances => _instances;
        public static Action<Speaker> onSpeakerAdded = null;
        public static Action<Speaker> onSpeakerRemoved = null;

        /// <summary>
        /// 0 = no priority. Higher turns down lower-priority audio (other stages, music, ambience...) near this speaker
        /// while its stream plays (see <see cref="AudioPriority"/>). A Speaker's own priority wins over an AudioChannel
        /// marker above it: stream audio never goes through the marker's channel.
        /// </summary>
        public int Priority
        {
            get => _priority;
            set { if (_priority == value) { return; } _priority = value; AudioPriority.onPriorityChanged?.Invoke(this); }
        }

        /// <summary>0..1: how far this speaker goes down when higher-priority audio is at full strength. Default 0.05.</summary>
        public float DuckFloor
        {
            get => _duckFloor;
            set { value = AudioPriority.ClampFloor(value); if (_duckFloor == value) { return; } _duckFloor = value; AudioPriority.onPriorityChanged?.Invoke(this); }
        }

        /// <summary>Sets Priority and Duck floor from layout data (stageMedia.speakerPriority / speakerDuckFloor).</summary>
        public void ConfigureAudioPriority(int priority, float duckFloor)
        {
            Priority = priority;
            DuckFloor = duckFloor;
        }

        // Nested under a Stage inherits the parent's index; a standalone speaker uses its own
        // _stageIndex. Lazily resolve the parent so layout spawn-then-reparent ordering still inherits.
        public int StageIndex
        {
            get
            {
                if (_stage == null) { _stage = GetComponentInParent<Stage>(); }
                return _stage != null ? _stage.StageIndex : _stageIndex;
            }
        }
        public int StreamIndex => _streamIndex;
        [Obsolete("Use StreamIndex.")]
        public SpeakerSource Source => _streamIndex == 1 ? SpeakerSource.Visuals : SpeakerSource.Artist;
        public SpeakerChannel Channel => _channel;
        public AudioSource AudioSource => _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_stage == null)
            {
                _stage = this.gameObject.GetComponentInParent<Stage>();
            }
            // Registration intentionally deferred to Configure()/Start() — see Configure().
        }

        private void Start()
        {
            // Fallback for authored (prefab-placed) speakers never Configure()'d by the layout:
            // register with the serialized values. Idempotent — a no-op when Configure() already did.
            AddSpeaker(this);
        }

        /// <summary>
        /// Initializes this speaker from layout data. Safe to call after Awake/AddComponent.
        /// stageIndex is the explicit target stage (ignored when nested under a Stage);
        /// speakerSource names the stream (see StageStreams); speakerChannel maps to the SpeakerChannel enum
        /// order (0 = Mono, 1 = StereoLeft, 2 = StereoRight).
        /// </summary>
        public void Configure(int stageIndex, string speakerSource, int speakerChannel)
        {
            _stageIndex = stageIndex;
            _streamIndex = StageStreams.Parse(speakerSource);
            _channel = ParseChannel(speakerChannel);
            // Register AFTER the authored stage/source/channel are set (the UWE LayoutLoader adds this
            // component then Configures one step later); else StageCoreConnector would bind the
            // pre-Configure defaults. Idempotent — the Start() fallback no-ops once this has run.
            AddSpeaker(this);
        }

        private static SpeakerChannel ParseChannel(int speakerChannel)
        {
            if (speakerChannel == (int)SpeakerChannel.StereoLeft) { return SpeakerChannel.StereoLeft; }
            if (speakerChannel == (int)SpeakerChannel.StereoRight) { return SpeakerChannel.StereoRight; }
            return SpeakerChannel.Mono;
        }

        private void OnValidate()
        {
            if (_stage == null)
            {
                _stage = this.gameObject.GetComponentInParent<Stage>();
            }
            if (_stage != null) { _stageIndex = _stage.StageIndex; }
            _duckFloor = AudioPriority.ClampFloor(_duckFloor);
            if (Application.isPlaying) { AudioPriority.onPriorityChanged?.Invoke(this); }  // Inspector edits in Play mode
        }

        private static void AddSpeaker(Speaker speaker)
        {
            if (_instances.Contains(speaker))
            {
                return;
            }
            _instances.Add(speaker);
            onSpeakerAdded?.Invoke(speaker);
        }

        private void OnDestroy()
        {
            if (_instances.Remove(this)) { onSpeakerRemoved?.Invoke(this); }
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(Speaker))]
    [UnityEditor.CanEditMultipleObjects]
    public class SpeakerEditor : UnityEditor.Editor
    {
        // One editor preference for every Speaker (nothing is saved in the scene or the world).
        public const string ShowAudioRangePrefKey = "VirtualVenues.Speaker.ShowAudioRange";
        public const string ShowAudioRangeLabel = "Show audio range";
        public const string FullVolumeLabel = "Full volume";
        public const string SilentBeyondLabel = "Silent beyond";
        private static readonly Color FullVolumeColor = new Color(0.2f, 0.85f, 0.35f);
        private static readonly Color SilentBeyondColor = new Color(1f, 0.55f, 0.1f);

        public static bool ShowAudioRange
        {
            get => UnityEditor.EditorPrefs.GetBool(ShowAudioRangePrefKey, false);
            set => UnityEditor.EditorPrefs.SetBool(ShowAudioRangePrefKey, value);
        }

        public override void OnInspectorGUI()
        {
            StagePickerGUI.DrawInspector(serializedObject);

            serializedObject.Update();
            AudioPriorityGUI.Draw(serializedObject);
            serializedObject.ApplyModifiedProperties();

            UnityEditor.EditorGUILayout.Space(4);
            UnityEditor.EditorGUI.BeginChangeCheck();
            bool show = UnityEditor.EditorGUILayout.Toggle(new GUIContent(ShowAudioRangeLabel,
                "Draws each Speaker's range in the Scene view: full volume inside the inner sphere (the AudioSource's Min Distance), " +
                "fading to silent at the outer sphere (Max Distance). An editor preference, not saved in the world."), ShowAudioRange);
            if (UnityEditor.EditorGUI.EndChangeCheck())
            {
                ShowAudioRange = show;
                UnityEditor.SceneView.RepaintAll();
            }
            if (targets.Length == 1 && target is Speaker speaker && speaker.TryGetComponent(out AudioSource source))
            {
                GetAudioRange(source, out float inner, out float outer);
                UnityEditor.EditorGUILayout.HelpBox($"{FullVolumeLabel} within {inner:0.#} m, fading to silent at {outer:0.#} m. " +
                    "Set them with the AudioSource's Min Distance and Max Distance.", UnityEditor.MessageType.None);
            }
        }

        /// <summary>
        /// The radii the app uses for a Speaker: full volume inside the AudioSource's Min Distance, a straight fade to silent at
        /// its Max Distance (StreamAudioDistanceLerper in the app). The outer radius is never smaller than the inner one.
        /// </summary>
        public static void GetAudioRange(AudioSource source, out float inner, out float outer)
        {
            inner = Mathf.Max(0f, source.minDistance);
            outer = Mathf.Max(inner, source.maxDistance);
        }

        [UnityEditor.DrawGizmo(UnityEditor.GizmoType.Selected | UnityEditor.GizmoType.NonSelected)]
        private static void DrawAudioRange(Speaker speaker, UnityEditor.GizmoType gizmoType)
        {
            if (!ShowAudioRange || !speaker.TryGetComponent(out AudioSource source)) { return; }
            GetAudioRange(source, out float inner, out float outer);
            Vector3 center = speaker.transform.position;
            bool selected = (gizmoType & UnityEditor.GizmoType.Selected) != 0;
            float fill = selected ? 0.12f : 0.05f;

            DrawSphere(center, outer, SilentBeyondColor, fill);
            DrawSphere(center, inner, FullVolumeColor, fill * 1.5f);

            GUIStyle style = new GUIStyle(UnityEditor.EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = FullVolumeColor;
            UnityEditor.Handles.Label(center + Vector3.up * inner, $"{FullVolumeLabel} ({inner:0.#} m)", style);
            style.normal.textColor = SilentBeyondColor;
            UnityEditor.Handles.Label(center + Vector3.up * outer, $"{SilentBeyondLabel} ({outer:0.#} m)", style);
        }

        private static void DrawSphere(Vector3 center, float radius, Color color, float fillAlpha)
        {
            if (radius <= 0f) { return; }
            Gizmos.color = new Color(color.r, color.g, color.b, fillAlpha);
            Gizmos.DrawSphere(center, radius);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.9f);
            Gizmos.DrawWireSphere(center, radius);
        }
    }
#endif
}
