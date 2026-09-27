using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>How the rider sits on a <see cref="Mount"/>. Order matches the core project's animation enum.</summary>
    public enum MountRidePose { Drive, Sit, Boardride }

    /// <summary>
    /// A rideable cosmetic (scooter, hover board, ...). Publish the prefab with the Avatar Publisher into the
    /// slot named exactly "Mount"; players summon it from their hotbar and ride it. Other players can hop on
    /// the <see cref="PassengerSeats"/>. Data only — the core project wires the runtime behaviour.
    /// </summary>
    public class Mount : MonoBehaviour
    {
        [Tooltip("Where the rider sits (position + facing). Defaults to this object.")]
        [SerializeField] private Transform _driverSeat = null;
        [Tooltip("Riding speed (walking is about 4, a scooter about 7).")]
        [SerializeField] private float _speed = 7f;
        [Tooltip("The rider's animation while riding.")]
        [SerializeField] private MountRidePose _ridePose = MountRidePose.Drive;
        [Tooltip("Optional seats other players can take. Leave empty for a single-rider mount.")]
        [SerializeField] private Transform[] _passengerSeats = new Transform[0];

        public Transform DriverSeat => _driverSeat != null ? _driverSeat : transform;
        public float Speed => _speed;
        public MountRidePose RidePose => _ridePose;
        public Transform[] PassengerSeats => _passengerSeats ?? new Transform[0];

#if UNITY_EDITOR
        // Example board (Resources/Mount.prefab): a flat deck with a DriverSeat child.
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Mount", isValidateFunction: false, priority: 0)]
        private static void CreateMount(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("Mount", Vector3.zero);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            DrawSeat(DriverSeat);
            Gizmos.color = Color.cyan;
            foreach (Transform seat in PassengerSeats) { DrawSeat(seat); }
        }

        private static void DrawSeat(Transform seat)
        {
            if (seat == null) { return; }
            Gizmos.DrawWireSphere(seat.position, 0.15f);
            Gizmos.DrawRay(seat.position, seat.forward * 0.5f);
        }
#endif
    }
}
