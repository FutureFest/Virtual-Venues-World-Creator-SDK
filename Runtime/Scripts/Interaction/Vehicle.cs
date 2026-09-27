using System;
using System.Text;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>One wheel of a <see cref="Vehicle"/>: the visual mesh plus what the wheel does.</summary>
    [Serializable]
    public struct VehicleWheel
    {
        [Tooltip("The wheel mesh. A physics wheel is placed at its position; it spins and steers with it.")]
        public Transform visual;
        [Tooltip("Turns left/right with steering (usually the front wheels).")]
        public bool steer;
        [Tooltip("Pushes the vehicle (usually the rear wheels). The vehicle only accelerates while a drive wheel touches the ground.")]
        public bool drive;
    }

    /// <summary>How a <see cref="Vehicle"/> drives.</summary>
    [Serializable]
    public class VehicleStats
    {
        [Tooltip("Top forward speed, metres per second.")]
        [Min(0.1f)] public float topSpeed = 15f;
        [Tooltip("How quickly it reaches top speed.")]
        [Min(0.1f)] public float acceleration = 7f;
        [Tooltip("How tightly it turns.")]
        [Min(0.1f)] public float steer = 4f;
        [Tooltip("Side grip. 1 = on rails, lower = slides more.")]
        [Range(0f, 1f)] public float grip = 0.95f;
        [Tooltip("Rigidbody mass in kg.")]
        [Min(1f)] public float mass = 250f;
        [Tooltip("Centre of mass, local to the vehicle. Lower = harder to flip.")]
        public Vector3 centerOfMass = new Vector3(0f, -0.2f, 0f);

        public VehicleStats Clone() { return (VehicleStats)MemberwiseClone(); }
    }

    /// <summary>
    /// A drivable world vehicle. Press the Interactable's key to take a seat — seat 0 drives, the rest ride —
    /// press again to get out. Data only: the core project adds the physics, wheels, networking and camera at
    /// runtime. Needs an <see cref="Interactable"/> (with a trigger collider) on the same object; adding a
    /// Vehicle in the Inspector adds one. Not [RequireComponent]: the world editor must be able to remove the
    /// Interactable while editing.
    /// </summary>
    public class Vehicle : InteractableData
    {
        private static InstanceTracker<Vehicle> _tracker = new InstanceTracker<Vehicle>();
        public static InstanceTracker<Vehicle> Tracker => _tracker;

        /// <summary>Raised when a vehicle is enabled, disabled or re-configured. The runtime (re)builds or tears down on it.</summary>
        public static event Action<Vehicle> OnChanged;

        [Tooltip("Seat 0 is the driver; the rest are passengers. Empty = one driver seat at the offset below.")]
        [SerializeField] private Transform[] _seats = new Transform[0];
        [Tooltip("Driver seat position (local) when no seats are assigned.")]
        [SerializeField] private Vector3 _driverSeatOffset = new Vector3(0f, 0.5f, 0f);
        [Tooltip("Usually 4. Empty = four wheels are placed at the corners of the vehicle's bounds.")]
        [SerializeField] private VehicleWheel[] _wheels = new VehicleWheel[0];
        [Tooltip("Physics wheel radius in metres.")]
        [Min(0.05f)] [SerializeField] private float _wheelRadius = 0.3f;
        [SerializeField] private VehicleStats _stats = new VehicleStats();
        [Header("Driver camera")]
        [Min(1f)] [SerializeField] private float _cameraDistance = 6f;
        [SerializeField] private float _cameraHeight = 2f;

        private string _vehicleKey = null;
        private Transform _ownedSeat = null;

        public override InteractbaleType InteractableType => InteractbaleType.Vehicle;
        public Transform[] AuthoredSeats => _seats;
        public VehicleWheel[] Wheels => _wheels;
        public float WheelRadius => _wheelRadius;
        public VehicleStats Stats => _stats;
        public float CameraDistance => _cameraDistance;
        public float CameraHeight => _cameraHeight;
        public Vector3 DriverSeatOffset => _driverSeatOffset;

        /// <summary>The authored seats, or one hidden driver seat at <see cref="DriverSeatOffset"/> when none are assigned.</summary>
        public Transform[] Seats
        {
            get
            {
                if (_seats != null && _seats.Length > 0 && _seats[0] != null) { return _seats; }
                if (_ownedSeat == null)
                {
                    _ownedSeat = new GameObject("DriverSeat").transform;
                    _ownedSeat.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    _ownedSeat.SetParent(transform, false);
                }
                _ownedSeat.localPosition = _driverSeatOffset;
                _ownedSeat.localRotation = Quaternion.identity;
                return new[] { _ownedSeat };
            }
        }

        /// <summary>
        /// Identifies this vehicle identically on every client: the configured key (world layouts), else the
        /// scene + sibling-index path. Read at use time — never cache it before <see cref="Configure"/> ran.
        /// </summary>
        public string VehicleKey
        {
            get
            {
                if (string.IsNullOrEmpty(_vehicleKey)) { _vehicleKey = BuildHierarchyKey(transform); }
                return _vehicleKey;
            }
        }

        private void Reset()
        {
            if (GetComponent<Interactable>() == null) { gameObject.AddComponent<Interactable>(); }
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
            if (_ownedSeat != null) { Destroy(_ownedSeat.gameObject); }
        }

        /// <summary>
        /// Set from external data (a loaded world layout). Authored seats/wheels are never touched; the
        /// driver-seat offset only applies when no seats are assigned. Safe to call repeatedly.
        /// </summary>
        public void Configure(string vehicleKey, VehicleStats stats, Vector3 driverSeatOffset, float cameraDistance, float cameraHeight)
        {
            if (!string.IsNullOrEmpty(vehicleKey)) { _vehicleKey = vehicleKey; }
            if (stats != null) { _stats = stats.Clone(); }
            _driverSeatOffset = driverSeatOffset;
            _cameraDistance = Mathf.Max(1f, cameraDistance);
            _cameraHeight = cameraHeight;
            OnChanged?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Transform[] seats = _seats != null && _seats.Length > 0 ? _seats : null;
            if (seats == null) { Gizmos.DrawWireSphere(transform.TransformPoint(_driverSeatOffset), 0.2f); }
            else
            {
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null) { continue; }
                    Gizmos.color = i == 0 ? Color.green : Color.cyan; // green = driver
                    Gizmos.DrawWireSphere(seats[i].position, 0.2f);
                    Gizmos.DrawRay(seats[i].position, seats[i].forward * 0.5f);
                }
            }

            Gizmos.color = Color.yellow;
            for (int i = 0; _wheels != null && i < _wheels.Length; i++)
            {
                if (_wheels[i].visual != null) { Gizmos.DrawWireSphere(_wheels[i].visual.position, _wheelRadius); }
            }
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.TransformPoint(_stats.centerOfMass), 0.05f);
        }

#if UNITY_EDITOR
        // Example kart (Resources/GoKart.prefab): primitive body, 4 wheels, driver seat, trigger approach volume.
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Go Kart", isValidateFunction: false, priority: 0)]
        private static void CreateGoKart(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("GoKart", Vector3.zero);
        }
#endif

        private static string BuildHierarchyKey(Transform t)
        {
            StringBuilder sb = new StringBuilder();
            for (Transform c = t; c != null; c = c.parent)
            {
                sb.Insert(0, "/" + c.GetSiblingIndex());
            }
            return t.gameObject.scene.name + sb;
        }
    }
}
