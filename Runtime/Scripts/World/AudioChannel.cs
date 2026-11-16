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
        public const string Music = "music";
        public const string Ambience = "ambience";
        public const string SoundEffects = "sfx";
        public const string Interface = "ui";

        [Obsolete("Renamed to AudioChannels.Music (id \"music\"). The old id still routes there.")]
        public const string BackgroundMusic = Music;
        [Obsolete("Renamed to AudioChannels.Ambience (id \"ambience\"). The old id still routes there.")]
        public const string AmbientAudio = Ambience;

        /// <summary>All default channels, in display order.</summary>
        public static readonly string[] Defaults =
            { Master, Stream, Voice, Music, Ambience, SoundEffects, Interface };

        /// <summary>Default channels a world's AudioSources (and custom channels) may use.</summary>
        public static readonly string[] Assignable = { Music, Ambience, SoundEffects };

        // Ids from older SDKs (0.10.13-0.10.19), still accepted on markers and custom-channel parents.
        private static readonly Dictionary<string, string> _oldIds = new Dictionary<string, string>
        {
            { "rides", SoundEffects },
            { "ambient-music", Music },
            { "background-music", Music },
            { "ambient-environment", Ambience },
            { "ambient-audio", Ambience },
        };

        /// <summary>The current id for an id from an older SDK; any other id comes back unchanged.</summary>
        public static string Canonical(string id) => id != null && _oldIds.TryGetValue(id, out string now) ? now : id;

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
                case Music: return "Music";
                case Ambience: return "Ambience";
                case SoundEffects: return "Sound Effects";
                case Interface: return "Interface";
                default: return id;
            }
        }
    }

    /// <summary>
    /// Marker: send every AudioSource on this GameObject and its children to one audio channel, so players
    /// can turn it up or down on its own. Use a built-in channel (Music, Ambience, Sound Effects) or a custom
    /// one from the world's <see cref="AudioChannelSet"/>. A nearer AudioChannel wins.
    /// Untagged sources: under an <see cref="AmbientAudio"/> component = Ambience, everything else = Sound Effects.
    /// Music is only used through this marker. Stage Speakers always use Stream. Old ids still work:
    /// background-music / ambient-music = Music, ambient-audio / ambient-environment = Ambience, rides = Sound Effects.
    /// The VirtualVenues app does the routing when the world loads; nothing changes at edit time.
    /// Priority / Duck floor: see <see cref="AudioPriority"/>; 0 (the default) changes nothing.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Audio Channel")]
    public class AudioChannel : MonoBehaviour
    {
        [Tooltip("Channel id: music, ambience, sfx, or a custom id from the world's Audio Channel Set.")]
        [SerializeField] private string _channel = AudioChannels.SoundEffects;
        [Tooltip(AudioPriority.PriorityTooltip)]
        [SerializeField] private int _priority = AudioPriority.DefaultPriority;
        [Tooltip(AudioPriority.DuckFloorTooltip)]
        [SerializeField, Range(0f, 1f)] private float _duckFloor = AudioPriority.DefaultDuckFloor;

        public string Channel => _channel;

        /// <summary>0 = no priority. Higher turns down lower-priority audio near it while it plays (see <see cref="AudioPriority"/>).
        /// Speakers below this marker use their own priority.</summary>
        public int Priority
        {
            get => _priority;
            set { if (_priority == value) { return; } _priority = value; AudioPriority.onPriorityChanged?.Invoke(this); }
        }

        /// <summary>0..1: how far these sources go down when higher-priority audio is at full strength. Default 0.05.</summary>
        public float DuckFloor
        {
            get => _duckFloor;
            set { value = AudioPriority.ClampFloor(value); if (_duckFloor == value) { return; } _duckFloor = value; AudioPriority.onPriorityChanged?.Invoke(this); }
        }

        private static readonly List<AudioChannel> _instances = new List<AudioChannel>();
        public static List<AudioChannel> Instances => _instances;
        public static Action<AudioChannel> onAudioChannelAdded = null;
        public static Action<AudioChannel> onAudioChannelRemoved = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instances.Clear();
            onAudioChannelAdded = null;
            onAudioChannelRemoved = null;
        }

        private void Awake()
        {
            if (_instances.Contains(this)) { return; }
            _instances.Add(this);
            onAudioChannelAdded?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (_instances.Remove(this)) { onAudioChannelRemoved?.Invoke(this); }
        }

        private void OnValidate()
        {
            _duckFloor = AudioPriority.ClampFloor(_duckFloor);
            if (Application.isPlaying) { AudioPriority.onPriorityChanged?.Invoke(this); }  // Inspector edits in Play mode
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
