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
    }
}
