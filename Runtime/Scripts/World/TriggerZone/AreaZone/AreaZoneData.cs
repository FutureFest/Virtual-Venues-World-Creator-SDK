using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class AreaZoneData : TriggerZoneData
    {
        [SerializeField] private string _areaId = "default-area";
        [SerializeField] private string _prompt = "";
        [SerializeField] private string _targetRoomId = "";

        public override TriggerZoneType ZoneType => TriggerZoneType.Area;

        public string AreaId => _areaId;
        public string Prompt => _prompt;
        public string TargetRoomId => _targetRoomId;

        /// <summary>
        /// Initializes this area zone from layout data. Safe to call after Awake/AddComponent.
        /// </summary>
        public void Configure(string areaId, string prompt, string targetRoomId)
        {
            _areaId = areaId;
            _prompt = prompt;
            _targetRoomId = targetRoomId;
        }
    }
}
