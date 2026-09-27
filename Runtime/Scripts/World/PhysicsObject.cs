using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// A physics prop everyone sees in the same place — a giant ball to push around, a crate that tumbles.
    /// Data only: the core project adds the Rigidbody and simulates it on the server, then syncs the pose to
    /// every client. Needs a (non-trigger) Collider on the same object. Bounce/push zones move it too.
    /// </summary>
    public class PhysicsObject : MonoBehaviour
    {
        private static InstanceTracker<PhysicsObject> _tracker = new InstanceTracker<PhysicsObject>();
        public static InstanceTracker<PhysicsObject> Tracker => _tracker;

        /// <summary>Raised when an object is enabled, disabled or re-configured. The runtime (re)builds or tears down on it.</summary>
        public static event Action<PhysicsObject> OnChanged;

        [Tooltip("Rigidbody mass in kg.")]
        [Min(0.01f)] [SerializeField] private float _mass = 10f;
        [Tooltip("Goes back to where it started when it falls below this height (world y).")]
        [SerializeField] private float _respawnBelowY = -50f;
        [Tooltip("Goes back to where it started after resting away from it this many seconds. 0 = never.")]
        [Min(0f)] [SerializeField] private float _respawnAfterIdle = 0f;
        [Tooltip("Players shove it by walking into it.")]
        [SerializeField] private bool _pushableByPlayers = true;

        private string _objectKey = null;

        public float Mass => _mass;
        public float RespawnBelowY => _respawnBelowY;
        public float RespawnAfterIdle => _respawnAfterIdle;
        public bool PushableByPlayers => _pushableByPlayers;

        /// <summary>Identifies this object identically on every client. See <see cref="Vehicle.VehicleKey"/>.</summary>
        public string ObjectKey
        {
            get
            {
                if (string.IsNullOrEmpty(_objectKey)) { _objectKey = Vehicle.BuildHierarchyKey(transform); }
                return _objectKey;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracker.Clear();
        }

        private void Awake()
        {
            _tracker.AddInstance(this);
        }

        private void OnEnable()
        {
            OnChanged?.Invoke(this);
        }

        private void OnDisable()
        {
            OnChanged?.Invoke(this);
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
        }

        /// <summary>Set from external data (a loaded world layout). Safe to call repeatedly.</summary>
        public void Configure(string objectKey, float mass, float respawnBelowY, float respawnAfterIdle, bool pushableByPlayers)
        {
            if (!string.IsNullOrEmpty(objectKey)) { _objectKey = objectKey; }
            _mass = Mathf.Max(0.01f, mass);
            _respawnBelowY = respawnBelowY;
            _respawnAfterIdle = Mathf.Max(0f, respawnAfterIdle);
            _pushableByPlayers = pushableByPlayers;
            OnChanged?.Invoke(this);
        }

#if UNITY_EDITOR
        // Example (Resources/PhysicsBall.prefab): a 2 m primitive ball to push around.
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Physics Ball", isValidateFunction: false, priority: 0)]
        private static void CreatePhysicsBall(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("PhysicsBall", Vector3.zero);
        }
#endif
    }
}
