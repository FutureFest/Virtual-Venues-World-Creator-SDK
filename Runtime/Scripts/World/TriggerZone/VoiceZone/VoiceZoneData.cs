using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class VoiceZoneData : TriggerZoneData
    {
        [SerializeField] private float _conversationalDistance = 5f;
        [SerializeField] private float _audibleDistance = 32f;

        public override TriggerZoneType ZoneType => TriggerZoneType.Voice;

        public float ConversationalDistance => _conversationalDistance;
        public float AudibleDistance => _audibleDistance;

        /// <summary>
        /// Initializes this voice zone from layout data. Safe to call after Awake/AddComponent.
        /// </summary>
        public void Configure(float conversationalDistance, float audibleDistance)
        {
            _conversationalDistance = conversationalDistance;
            _audibleDistance = audibleDistance;
        }
    }
}
