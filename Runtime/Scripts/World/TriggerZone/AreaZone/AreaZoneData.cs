using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class AreaZoneData : TriggerZoneData
    {
        [SerializeField] private string _areaId = "default-area";

        public override TriggerZoneType ZoneType => TriggerZoneType.Area;

        public string AreaId => _areaId;
    }
}
