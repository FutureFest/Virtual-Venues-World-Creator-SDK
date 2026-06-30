using System.Collections.Generic;
using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public enum SpeakerSource
    {
        Artist,
        Visuals
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
        [SerializeField] private SpeakerSource _source = SpeakerSource.Artist;
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
        public SpeakerSource Source => _source;
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
        /// speakerSource is "Artist" or "Visuals"; speakerChannel maps to the SpeakerChannel enum order
        /// (0 = Mono, 1 = StereoLeft, 2 = StereoRight).
        /// </summary>
        public void Configure(int stageIndex, string speakerSource, int speakerChannel)
        {
            _stageIndex = stageIndex;
            _source = ParseSource(speakerSource);
            _channel = ParseChannel(speakerChannel);
            // Register AFTER the authored stage/source/channel are set (the UWE LayoutLoader adds this
            // component then Configures one step later); else StageCoreConnector would bind the
            // pre-Configure defaults. Idempotent — the Start() fallback no-ops once this has run.
            AddSpeaker(this);
        }

        private static SpeakerSource ParseSource(string speakerSource)
        {
            if (speakerSource == "Visuals") { return SpeakerSource.Visuals; }
            return SpeakerSource.Artist;
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
    }
}
