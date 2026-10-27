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

        private AudioSource _audioSource = null;

        public static List<Speaker> Instances => _instances;
        public static Action<Speaker> onSpeakerAdded = null;

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
            _instances.Remove(this);
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(Speaker))]
    [UnityEditor.CanEditMultipleObjects]
    public class SpeakerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() { StagePickerGUI.DrawInspector(serializedObject); }
    }
#endif
}
