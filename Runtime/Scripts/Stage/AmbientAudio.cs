using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Ambient sound for an area. It fades out with the player's distance (the AudioSource's min/max distance)
    /// and goes quiet while the player is inside a <see cref="RoomBoundary"/> room. The app sends it to the
    /// Ambient Audio channel. Silent in the editor: it waits for the app to report the player's position.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Ambient Audio")]
    [RequireComponent(typeof(AudioSource))]
    public class AmbientAudio : MonoBehaviour
    {
        private AudioSource _source;
        private bool _originalMute;
        private float _originalVolume;
        private float _originalSpatialBlend;

        private void OnEnable()
        {
            _source = GetComponent<AudioSource>(); _originalMute = _source.mute;
            _originalVolume = _source.volume; _originalSpatialBlend = _source.spatialBlend;
            // Use the avatar for distance as well, not the stage-repositioned AudioListener.
            _source.spatialBlend = 0f;
            _source.mute = true;
        }

        private void LateUpdate()
        {
            // Runtime supplies the avatar position: the stage audio listener can be repositioned.
            Vector3? listener = RoomBoundary.ListenerPosition?.Invoke();
            _source.mute = _originalMute || !listener.HasValue || RoomBoundary.RoomAt(listener.Value, true) >= 0;
            if (listener.HasValue)
                _source.volume = _originalVolume * (1f - Mathf.InverseLerp(_source.minDistance,
                    _source.maxDistance, Vector3.Distance(listener.Value, _source.transform.position)));
        }

        private void OnDisable()
        {
            if (!_source) { return; }
            _source.mute = _originalMute; _source.volume = _originalVolume; _source.spatialBlend = _originalSpatialBlend;
        }
    }
}
