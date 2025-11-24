using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public abstract class TriggerZoneData : MonoBehaviour
    {
        public enum TriggerZoneType
        {
            None,
            Voice,
            Area,
            Swim,
        }

        public abstract TriggerZoneType ZoneType { get; }
    }
}
