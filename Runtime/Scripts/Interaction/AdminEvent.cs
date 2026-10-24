using System;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// A named cue an admin fires for everyone in the world by typing <c>/event &lt;name&gt;</c> in chat (fireworks,
    /// a light show, a curtain drop). <c>/events</c> lists the names in the current world. Several markers may share
    /// a name; all of them fire. Names are case-insensitive. <see cref="Cooldown"/> is enforced by the server.
    /// One-shot: players who join later don't replay it. Data only — the core project wires the runtime behaviour.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Admin Event")]
    public class AdminEvent : MonoBehaviour
    {
        private static InstanceTracker<AdminEvent> _tracker = new InstanceTracker<AdminEvent>();
        public static InstanceTracker<AdminEvent> Tracker => _tracker;

        [Tooltip("What the admin types after /event. Case-insensitive; markers with the same name fire together.")]
        [SerializeField] private string _eventName = "fireworks";
        [Tooltip("Runs for everyone in the world when an admin fires this event.")]
        [SerializeField] private UnityEvent _onEveryone = new UnityEvent();
        [Tooltip("Seconds before this name can fire again (enforced by the server).")]
        [SerializeField, Min(0f)] private float _cooldown = 10f;
        [Tooltip("Optional. Shown next to the name in the admin's /events list.")]
        [SerializeField, TextArea] private string _description = "";

        public string EventName => _eventName == null ? "" : _eventName.Trim();
        public float Cooldown => _cooldown;
        public string Description => _description;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracker.Clear();
        }

        private void Awake()
        {
            _tracker.AddInstance(this);
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
        }

        /// <summary>The one name rule shared by the SDK and the app: trimmed, case-insensitive.</summary>
        public bool Matches(string eventName)
        {
            return eventName != null && EventName.Length > 0 && string.Equals(EventName, eventName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public void InvokeEveryone() => _onEveryone?.Invoke();

        /// <summary>Runs every marker named <paramref name="eventName"/> locally. Returns how many ran.</summary>
        public static int InvokeAll(string eventName)
        {
            _tracker.PruneNullInstances();
            int count = 0;
            // Copy: a listener may destroy or spawn markers.
            foreach (AdminEvent evt in _tracker.Instances.ToArray())
            {
                if (evt == null || !evt.Matches(eventName)) { continue; }
                evt.InvokeEveryone();
                count++;
            }
            return count;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Admin Event", isValidateFunction: false, priority: 0)]
        private static void CreateAdminEvent(UnityEditor.MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Admin Event (fireworks)");
            UnityEditor.GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            go.AddComponent<AdminEvent>();
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "New Admin Event");
            UnityEditor.Selection.activeObject = go;
        }
#endif
    }
}
