using System;
using System.Text;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Interact-to-sit. Press the Interactable's key to sit on <see cref="SeatPoint"/>, press again to stand.
    /// One rider per seat, synced for everyone. The rider follows the seat every frame, so a seat on a
    /// network-synced moving object (e.g. a <see cref="RotatingObject"/>) carries its rider along.
    /// Data only — the core project wires the runtime behaviour. Needs an <see cref="Interactable"/> (with a
    /// trigger collider) on the same object; adding a Seat in the Inspector adds one. Not [RequireComponent]:
    /// the world editor must be able to remove the Interactable while editing.
    /// </summary>
    public class Seat : InteractableData
    {
        private static InstanceTracker<Seat> _tracker = new InstanceTracker<Seat>();
        public static InstanceTracker<Seat> Tracker => _tracker;

        /// <summary>Raised when <see cref="Configure"/> changes the key or seat point (old key passed).</summary>
        public static event Action<Seat, string> OnConfigured;

        [Tooltip("Where the rider sits (position + facing). Defaults to this object.")]
        [SerializeField] private Transform _seatPoint = null;

        private string _seatKey = null;
        private bool _ownsSeatPoint = false;

        public override InteractbaleType InteractableType => InteractbaleType.Seat;
        public Transform SeatPoint => _seatPoint != null ? _seatPoint : transform;

        /// <summary>
        /// Identifies this seat identically on every client: the configured key (world layouts), else the
        /// scene + sibling-index path, which matches because every client loads the same world bundle.
        /// Cached on first read so a later reparent can't change it.
        /// </summary>
        public string SeatKey
        {
            get
            {
                if (string.IsNullOrEmpty(_seatKey)) { _seatKey = BuildHierarchyKey(transform); }
                return _seatKey;
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

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
            if (_ownsSeatPoint && _seatPoint != null) { Destroy(_seatPoint.gameObject); }
        }

        /// <summary>
        /// Set from external data (a loaded world layout). Places a child "SeatPoint" at a local offset
        /// with a yaw in degrees. Safe to call repeatedly.
        /// </summary>
        public void Configure(string seatKey, Vector3 localOffset, float yaw)
        {
            string oldKey = _seatKey;
            if (!string.IsNullOrEmpty(seatKey)) { _seatKey = seatKey; }

            if (_seatPoint == null || _seatPoint == transform)
            {
                _seatPoint = new GameObject("SeatPoint").transform;
                _seatPoint.gameObject.hideFlags = HideFlags.HideInHierarchy; // not a creator-editable child
                _seatPoint.SetParent(transform, false);
                _ownsSeatPoint = true;
            }
            if (_ownsSeatPoint) // never move a creator-assigned seat point (it may be a mesh or a grandchild)
            {
                _seatPoint.localPosition = localOffset;
                _seatPoint.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            OnConfigured?.Invoke(this, oldKey);
        }

#if UNITY_EDITOR
        // Example chair (Resources/Seat.prefab): cushion + backrest, trigger approach volume, SeatPoint child.
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Seat", isValidateFunction: false, priority: 0)]
        private static void CreateSeat(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("Seat", Vector3.zero);
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
