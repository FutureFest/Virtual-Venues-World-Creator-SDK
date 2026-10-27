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
        public const string BackgroundMusic = "background-music";
        public const string AmbientAudio = "ambient-audio";
        public const string SoundEffects = "sfx";
        public const string Interface = "ui";

        /// <summary>All default channels, in display order.</summary>
        public static readonly string[] Defaults =
            { Master, Stream, Voice, BackgroundMusic, AmbientAudio, SoundEffects, Interface };

        /// <summary>Default channels a world's AudioSources (and custom channels) may use.</summary>
        public static readonly string[] Assignable = { BackgroundMusic, AmbientAudio, SoundEffects };

        // Ids from SDK 0.10.13-0.10.14, still accepted on markers and custom-channel parents.
        private static readonly Dictionary<string, string> _oldIds = new Dictionary<string, string>
        {
            { "rides", SoundEffects },
            { "ambient-music", BackgroundMusic },
            { "ambient-environment", AmbientAudio },
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
                case BackgroundMusic: return "Background Music";
                case AmbientAudio: return "Ambient Audio";
                case SoundEffects: return "Sound Effects";
                case Interface: return "Interface";
                default: return id;
            }
        }
    }

    /// <summary>
    /// Marker: send every AudioSource on this GameObject and its children to one audio channel, so players
    /// can turn it up or down on its own. Use a default channel (Background Music, Ambient Audio, Sound Effects)
    /// or a custom one from the world's <see cref="AudioChannelSet"/>. A nearer AudioChannel wins.
    /// Untagged sources: under an <see cref="AmbientAudio"/> = Ambient Audio, everything else = Sound Effects.
    /// Background Music is only used through this marker. Stage Speakers always use Stream. Old ids still work:
    /// rides = Sound Effects, ambient-music = Background Music, ambient-environment = Ambient Audio.
    /// The VirtualVenues app does the routing when the world loads; nothing changes at edit time.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Audio Channel")]
    public class AudioChannel : MonoBehaviour
    {
        [Tooltip("Channel id: background-music, ambient-audio, sfx, or a custom id from the Audio Channel Set.")]
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
