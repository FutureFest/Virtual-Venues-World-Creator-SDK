using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// The audio channel ids every VirtualVenues world has. Players set each channel's volume in the app
    /// (web overlay or in-game menu). A world can add its own channels with an <see cref="AudioChannelSet"/>.
    /// </summary>
    public static class AudioChannels
    {
        public const string Master = "master";
        public const string Stream = "stream";
        public const string Voice = "voice";
        public const string AmbientMusic = "ambient-music";
        public const string AmbientEnvironment = "ambient-environment";
        public const string SoundEffects = "sfx";
        public const string Rides = "rides";
        public const string Interface = "ui";

        /// <summary>All default channels, in display order.</summary>
        public static readonly string[] Defaults =
            { Master, Stream, Voice, AmbientMusic, AmbientEnvironment, SoundEffects, Rides, Interface };

        /// <summary>Default channels a world's AudioSources (and custom channels) may use.</summary>
        public static readonly string[] Assignable = { AmbientMusic, AmbientEnvironment, SoundEffects, Rides };

        /// <summary>How many custom channels one world may add.</summary>
        public const int MaxCustom = 8;

        private static readonly Regex _idRule = new Regex("^[a-z0-9][a-z0-9-]{0,31}$");

        /// <summary>Lowercase kebab-case, 1-32 characters.</summary>
        public static bool IsValidId(string id) => !string.IsNullOrEmpty(id) && _idRule.IsMatch(id);

        public static bool IsDefault(string id) => Array.IndexOf(Defaults, id) >= 0;
        public static bool IsAssignable(string id) => Array.IndexOf(Assignable, id) >= 0;

        public static string DefaultName(string id)
        {
            switch (id)
            {
                case Master: return "Master";
                case Stream: return "Stream";
                case Voice: return "Voice Chat";
                case AmbientMusic: return "Ambient Music";
                case AmbientEnvironment: return "Ambient Environment";
                case SoundEffects: return "Sound Effects";
                case Rides: return "Rides and Vehicles";
                case Interface: return "Interface";
                default: return id;
            }
        }
    }

    /// <summary>
    /// Marker: send every AudioSource on this GameObject and its children to one audio channel, so players
    /// can turn it up or down on its own. Use a default channel (Ambient Music, Ambient Environment,
    /// Sound Effects, Rides and Vehicles) or a custom one from the world's <see cref="AudioChannelSet"/>.
    /// A nearer AudioChannel wins. Untagged sources: Room Ambient Audio = Ambient Music, Ride Sounds and
    /// vehicles = Rides and Vehicles, everything else = Sound Effects. Stage Speakers always use Stream.
    /// The VirtualVenues app does the routing when the world loads; nothing changes at edit time.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Audio Channel")]
    public class AudioChannel : MonoBehaviour
    {
        [Tooltip("Channel id: ambient-music, ambient-environment, sfx, rides, or a custom id from the Audio Channel Set.")]
        [SerializeField] private string _channel = AudioChannels.SoundEffects;

        public string Channel => _channel;

        private static readonly List<AudioChannel> _instances = new List<AudioChannel>();
        public static List<AudioChannel> Instances => _instances;
        public static Action<AudioChannel> onAudioChannelAdded = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instances.Clear();
            onAudioChannelAdded = null;
        }

        private void Awake()
        {
            if (_instances.Contains(this)) { return; }
            _instances.Add(this);
            onAudioChannelAdded?.Invoke(this);
        }

        private void OnDestroy()
        {
            _instances.Remove(this);
        }

        /// <summary>Fills <paramref name="sources"/> with the AudioSources whose nearest AudioChannel is this one.</summary>
        public void GetSources(List<AudioSource> sources)
        {
            sources.Clear();
            foreach (AudioSource src in GetComponentsInChildren<AudioSource>(true))
            {
                if (src.GetComponentInParent<AudioChannel>(true) == this) { sources.Add(src); }
            }
        }
    }
}
