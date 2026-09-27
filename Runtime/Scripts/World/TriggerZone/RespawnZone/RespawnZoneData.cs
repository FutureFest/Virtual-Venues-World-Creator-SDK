using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Sends players who enter back to a respawn point (e.g. under the map, or a lava pit). Data only — the
    /// core project adds the respawn. Put it next to a <see cref="TriggerZone"/> and a trigger collider.
    /// </summary>
    public class RespawnZoneData : TriggerZoneData
    {
        [Tooltip("Where players are sent. Empty = a world spawn point.")]
        [SerializeField] private Transform _respawnPoint = null;
        [Tooltip("Players who enter are sent to the respawn point. Off = a vehicle-only kill zone.")]
        [SerializeField] private bool _affectsPlayers = true;
        [Tooltip("Vehicles that enter drop their riders and return to where they started.")]
        [SerializeField] private bool _affectsVehicles = true;

        public override TriggerZoneType ZoneType => TriggerZoneType.Respawn;

        public Transform RespawnPoint => _respawnPoint;
        public bool AffectsPlayers => _affectsPlayers;
        public bool AffectsVehicles => _affectsVehicles;

        /// <summary>Initializes this zone from layout data. Safe to call after Awake/AddComponent.</summary>
        public void Configure(Transform respawnPoint, bool affectsVehicles)
        {
            Configure(respawnPoint, affectsVehicles, true);
        }

        /// <summary>Initializes this zone from layout data. Safe to call after Awake/AddComponent.</summary>
        public void Configure(Transform respawnPoint, bool affectsVehicles, bool affectsPlayers)
        {
            _respawnPoint = respawnPoint;
            _affectsVehicles = affectsVehicles;
            _affectsPlayers = affectsPlayers;
            RaiseChanged();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("GameObject/VirtualVenues/Respawn Zone", isValidateFunction: false, priority: 0)]
        private static void CreateRespawnZone(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("RespawnZone", Vector3.zero);
        }
#endif
    }
}
