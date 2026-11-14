using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    [Serializable]
    public class CustomAudioChannel
    {
        [Tooltip("Lowercase id, e.g. coaster-screams. Tag AudioSources with it through an Audio Channel.")]
        public string id = "";
        [Tooltip("Name players see in the volume panel.")]
        public string displayName = "";
        [Tooltip("Built-in channel it sits under (music, ambience or sfx). The parent's volume also turns this one down.")]
        public string parent = AudioChannels.SoundEffects;
        [Range(0f, 1f)] public float defaultVolume = 1f;
    }

    /// <summary>
    /// The world's own audio channels, on top of the built-in ones every world has (a world does not have to
    /// use any built-in one). One per world. Each custom channel shows as its own slider in the players' audio
    /// mixer, under its parent. At most <see cref="AudioChannels.MaxCustom"/>; the app ignores any past that.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Audio Channel Set")]
    [DisallowMultipleComponent]
    public class AudioChannelSet : MonoBehaviour
    {
        [SerializeField] private List<CustomAudioChannel> _channels = new List<CustomAudioChannel>();

        public List<CustomAudioChannel> Channels => _channels;

        private static readonly List<AudioChannelSet> _instances = new List<AudioChannelSet>();
        public static List<AudioChannelSet> Instances => _instances;
        public static Action<AudioChannelSet> onAudioChannelSetAdded = null;
        public static Action<AudioChannelSet> onAudioChannelSetRemoved = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instances.Clear();
            onAudioChannelSetAdded = null;
            onAudioChannelSetRemoved = null;
        }

        private void Awake()
        {
            if (_instances.Contains(this)) { return; }
            _instances.Add(this);
            onAudioChannelSetAdded?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (_instances.Remove(this)) { onAudioChannelSetRemoved?.Invoke(this); }
        }

        /// <summary>Adds a line to <paramref name="problems"/> for each channel the app will skip.</summary>
        public void Validate(List<string> problems)
        {
            problems.Clear();
            var seen = new HashSet<string>();
            for (int i = 0; i < _channels.Count; i++)
            {
                CustomAudioChannel c = _channels[i];
                string at = $"Channel {i + 1}";
                if (!AudioChannels.IsValidId(c.id)) { problems.Add($"{at}: id '{c.id}' must be lowercase letters, digits and '-', up to 32 characters."); continue; }
                if (AudioChannels.IsDefault(AudioChannels.Canonical(c.id))) { problems.Add($"{at}: '{c.id}' is a default channel id (or an old one)."); continue; }
                if (!seen.Add(c.id)) { problems.Add($"{at}: '{c.id}' is listed twice."); continue; }
                if (!AudioChannels.IsAssignable(AudioChannels.Canonical(c.parent))) { problems.Add($"{at}: parent must be one of {string.Join(", ", AudioChannels.Assignable)}."); }
                if (i >= AudioChannels.MaxCustom) { problems.Add($"{at}: only the first {AudioChannels.MaxCustom} custom channels are used."); }
            }
        }
    }
}
