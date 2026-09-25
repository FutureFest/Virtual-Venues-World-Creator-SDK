using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class RoomAmbientAudio : MonoBehaviour
    {
        private AudioSource source;
        private bool originalMute;
        private float originalVolume;
        private float originalSpatialBlend;
        private void OnEnable()
        {
            source = GetComponent<AudioSource>(); originalMute = source.mute;
            originalVolume = source.volume; originalSpatialBlend = source.spatialBlend;
            // Use the avatar for distance as well, not the stage-repositioned AudioListener.
            source.spatialBlend = 0f;
            source.mute = true;
        }
        private void LateUpdate()
        {
            // Runtime supplies the avatar position: the stage audio listener can be repositioned.
            Vector3? listener = RoomBoundary.ListenerPosition?.Invoke();
            source.mute = originalMute || !listener.HasValue || RoomBoundary.RoomAt(listener.Value, true) >= 0;
            if (listener.HasValue)
                source.volume = originalVolume * (1f - Mathf.InverseLerp(source.minDistance,
                    source.maxDistance, Vector3.Distance(listener.Value, source.transform.position)));
        }
        private void OnDisable()
        {
            if (!source) return;
            source.mute = originalMute; source.volume = originalVolume; source.spatialBlend = originalSpatialBlend;
        }
    }
}
