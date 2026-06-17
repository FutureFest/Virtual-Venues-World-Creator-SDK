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
        [Space]
        [SerializeField] private SpeakerSource _source = SpeakerSource.Artist;
        [SerializeField] private SpeakerChannel _channel = SpeakerChannel.Mono;

        private AudioSource _audioSource = null;

        public static List<Speaker> Instances => _instances;
        public static Action<Speaker> onSpeakerAdded = null;

        public int StageIndex => _stage != null ? _stage.StageIndex : 0;
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

            AddSpeaker(this);
        }

        /// <summary>
        /// Initializes this speaker from layout data. Safe to call after Awake/AddComponent.
        /// speakerSource is "Artist" or "Visuals"; speakerChannel maps to the SpeakerChannel enum order
        /// (0 = Mono, 1 = StereoLeft, 2 = StereoRight).
        /// </summary>
        public void Configure(string speakerSource, int speakerChannel)
        {
            _source = ParseSource(speakerSource);
            _channel = ParseChannel(speakerChannel);
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
