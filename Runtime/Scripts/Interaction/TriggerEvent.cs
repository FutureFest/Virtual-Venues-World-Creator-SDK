using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Fires UnityEvents when a player walks into this object's trigger collider (jump scares, sounds, doors).
    /// <see cref="_onLocalPlayerEnter"/> runs only for the player who walked in; <see cref="_onEveryone"/> runs on
    /// every client. <see cref="Cooldown"/> and <see cref="Once"/> are enforced by the server, so everyone sees one
    /// shared firing. Data only — the core project wires the runtime behaviour. Needs a trigger collider on this
    /// same object (adding the component adds one).
    /// </summary>
    public class TriggerEvent : MonoBehaviour
    {
        private static InstanceTracker<TriggerEvent> _tracker = new InstanceTracker<TriggerEvent>();
        public static InstanceTracker<TriggerEvent> Tracker => _tracker;

        [Tooltip("Runs only for the player who entered.")]
        [SerializeField] private UnityEvent _onLocalPlayerEnter = new UnityEvent();
        [Tooltip("Runs for everyone in the world.")]
        [SerializeField] private UnityEvent _onEveryone = new UnityEvent();
        [Tooltip("Seconds before it can fire again.")]
        [SerializeField, Min(0f)] private float _cooldown = 1f;
        [Tooltip("Fire only the first time anyone enters.")]
        [SerializeField] private bool _once = false;

        private string _eventKey = null;

        public float Cooldown => _cooldown;
        public bool Once => _once;

        /// <summary>
        /// Identical on every client: scene + sibling-index path (every client loads the same world).
        /// Cached on first read so a later reparent can't change it. Same scheme as <see cref="Seat.SeatKey"/>.
        /// </summary>
        public string EventKey
        {
            get
            {
                if (string.IsNullOrEmpty(_eventKey)) { _eventKey = BuildHierarchyKey(transform); }
                return _eventKey;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracker.Clear();
        }

        private void Reset()
        {
            if (GetComponent<Collider>() != null) { return; }
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
        }

        private void Awake()
        {
            _tracker.AddInstance(this);
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
        }

        public void InvokeLocalPlayerEnter() => _onLocalPlayerEnter?.Invoke();
        public void InvokeEveryone() => _onEveryone?.Invoke();

#if UNITY_EDITOR
        // Example trigger volume (Resources/TriggerEvent.prefab).
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Trigger Event", isValidateFunction: false, priority: 0)]
        private static void CreateTriggerEvent(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("TriggerEvent", Vector3.zero);
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
