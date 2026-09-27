using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Holds this object's starting world rotation while its parent spins: ferris-wheel pods, hanging lanterns.
    /// Nothing networked — it follows whatever its parent does (use a network-synced <see cref="RotatingObject"/>
    /// on the wheel so every client sees the same pods). <see cref="CarryPlayers"/> lets players ride it (core adds
    /// the platform; needs a solid collider to stand on).
    /// </summary>
    // After the movers (-50), before the player's platform probe (0): the pod is upright both when the probe reads
    // it in Update and when it records it in LateUpdate, so a rider sees one clean move per frame.
    [DefaultExecutionOrder(-40)]
    public class KeepUpright : MonoBehaviour
    {
        /// <summary>Raised when a KeepUpright is enabled; core listens to add the player carrier.</summary>
        public static event Action<KeepUpright> OnEnabled;

        [Tooltip("Players standing on this ride along with it (e.g. a ferris-wheel pod).")]
        [SerializeField] private bool _carryPlayers = true;

        private Quaternion _rotation = Quaternion.identity;

        public bool CarryPlayers => _carryPlayers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnEnabled = null;
        }

        private void OnEnable()
        {
            OnEnabled?.Invoke(this);
        }

        // Start, not Awake: a world loader may place the object after it is created.
        private void Start()
        {
            _rotation = transform.rotation;
        }

        // Both callbacks: Update covers parents spun in Update, LateUpdate covers Animator-driven parents.
        private void Update()
        {
            transform.rotation = _rotation;
        }

        private void LateUpdate()
        {
            transform.rotation = _rotation;
        }
    }
}
