using UnityEngine;
using UnityEngine.Events;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Launches players (and pushable objects) that touch this zone. Data only — the core project adds the bounce.
    /// Put it next to a <see cref="TriggerZone"/> and a trigger collider.
    /// </summary>
    public class BounceZoneData : TriggerZoneData
    {
        [Tooltip("Launch along this transform's forward (blue) axis. Empty = straight out of the pad (its up axis).")]
        [SerializeField] private Transform _direction = null;
        [SerializeField] private float _force = 20f;
        [Tooltip("Runs on every bounce, e.g. a sound or an animation trigger.")]
        [SerializeField] private UnityEvent _onBounce = new UnityEvent();

        public override TriggerZoneType ZoneType => TriggerZoneType.Bounce;

        public Transform Direction => _direction;
        public float Force => _force;
        public UnityEvent OnBounce => _onBounce;

        /// <summary>Initializes this zone from layout data. Safe to call after Awake/AddComponent.</summary>
        public void Configure(Transform direction, float force)
        {
            _direction = direction;
            _force = force;
            RaiseChanged();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("GameObject/VirtualVenues/Bounce Pad", isValidateFunction: false, priority: 0)]
        private static void CreateBouncePad(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("BouncePad", Vector3.zero);
        }
#endif
    }
}
