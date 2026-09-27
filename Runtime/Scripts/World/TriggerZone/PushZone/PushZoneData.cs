using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Pushes players (and pushable objects) while they are inside: a kick on entry, then a steady push.
    /// Data only — the core project adds the push. Put it next to a <see cref="TriggerZone"/> and a trigger collider.
    /// </summary>
    public class PushZoneData : TriggerZoneData
    {
        [Tooltip("Push along this transform's forward (blue) axis. Empty = this zone's forward.")]
        [SerializeField] private Transform _direction = null;
        [Tooltip("Kick applied once on entering.")]
        [SerializeField] private float _initialForce = 5f;
        [Tooltip("Push applied every physics step while inside.")]
        [SerializeField] private float _force = 1f;

        public override TriggerZoneType ZoneType => TriggerZoneType.Push;

        public Transform Direction => _direction;
        public float InitialForce => _initialForce;
        public float Force => _force;

        /// <summary>Initializes this zone from layout data. Safe to call after Awake/AddComponent.</summary>
        public void Configure(Transform direction, float initialForce, float force)
        {
            _direction = direction;
            _initialForce = initialForce;
            _force = force;
            RaiseChanged();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("GameObject/VirtualVenues/Push Zone", isValidateFunction: false, priority: 0)]
        private static void CreatePushZone(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("PushZone", Vector3.zero);
        }
#endif
    }
}
