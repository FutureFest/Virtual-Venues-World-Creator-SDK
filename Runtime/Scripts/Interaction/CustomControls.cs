using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualVenues.WorldCreator
{
    /// <summary>When a <see cref="CustomControls"/> shows its buttons. Append only — the value is serialized.</summary>
    public enum ControlsShowWhen
    {
        [Tooltip("While the local player sits on this seat (a Seat, a Vehicle seat Transform, or a Vehicle root for all its seats).")]
        WhileSeated = 0,
        [Tooltip("From pressing this object's Interactable key until pressing it again or walking away.")]
        AfterInteract = 1,
        [Tooltip("While this object's Interactable is highlighted (the player is close).")]
        WhileInRange = 2,
        [Tooltip("While the local player stands inside this object's trigger collider.")]
        WhileInZone = 3,
    }

    /// <summary>One on-screen button.</summary>
    [Serializable]
    public class CustomControl
    {
        public string label = "Button";
        [Tooltip("Optional icon shown on the button.")]
        public Sprite icon = null;
        [Tooltip("Seconds before it can be pressed again.")]
        [Min(0f)] public float cooldown = 0.2f;
        [Tooltip("Runs only for the player who pressed it.")]
        public UnityEvent onPressed = new UnityEvent();
        [Tooltip("Runs for everyone in the world. Put vehicle movement (Vehicle.Push / Vehicle.Turn) here — it only acts where the vehicle's physics runs.")]
        public UnityEvent onEveryone = new UnityEvent();
    }

    /// <summary>
    /// Creator-made on-screen buttons that appear in a context: while seated, after interacting, while in range,
    /// or while inside a zone (see <see cref="ShowWhen"/>). The newest shown set is on screen; when it hides, the
    /// previous one comes back. Data + activation only — the core project draws the buttons and networks the
    /// Everyone events; with no runtime router (TestPlayer/offline) Everyone runs locally.
    /// </summary>
    public class CustomControls : MonoBehaviour
    {
        private static InstanceTracker<CustomControls> _tracker = new InstanceTracker<CustomControls>();
        public static InstanceTracker<CustomControls> Tracker => _tracker;

        private static readonly List<CustomControls> _shown = new List<CustomControls>();

        /// <summary>Raised when <see cref="Current"/> may have changed.</summary>
        public static event Action OnCurrentChanged;

        /// <summary>
        /// Set by the core project to send an Everyone press to the server. Null (TestPlayer/offline) → runs locally.
        /// </summary>
        public static Action<CustomControls, int> EveryoneRouter;

        /// <summary>The set on screen now: the most recently shown one, or null.</summary>
        public static CustomControls Current => _shown.Count > 0 ? _shown[_shown.Count - 1] : null;

        [SerializeField] private ControlsShowWhen _showWhen = ControlsShowWhen.WhileSeated;
        [SerializeField] private CustomControl[] _controls = new CustomControl[0];

        private readonly Dictionary<int, float> _lastPressed = new Dictionary<int, float>();
        private string _controlsKey = null;
        private bool _interactShown = false;

        public ControlsShowWhen ShowWhen => _showWhen;
        public CustomControl[] Controls => _controls;
        public bool IsShown => _shown.Contains(this);

        /// <summary>Identical on every client: scene + sibling-index path. Same scheme as <see cref="TriggerEvent.EventKey"/>.</summary>
        public string ControlsKey
        {
            get
            {
                if (string.IsNullOrEmpty(_controlsKey)) { _controlsKey = BuildHierarchyKey(transform); }
                return _controlsKey;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracker.Clear();
            _shown.Clear();
            OnCurrentChanged = null;
            EveryoneRouter = null;
        }

        private void Reset()
        {
            bool seat = GetComponentInParent<Seat>() != null || GetComponentInParent<Vehicle>() != null;
            if (seat) { _showWhen = ControlsShowWhen.WhileSeated; return; }
            if (GetComponent<Interactable>() != null) { _showWhen = ControlsShowWhen.AfterInteract; return; }
            _showWhen = ControlsShowWhen.WhileInZone;
            if (GetComponent<Collider>() != null) { return; }
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
        }

        private void Awake()
        {
            _tracker.AddInstance(this);
        }

        private void Start()
        {
            if (_showWhen != ControlsShowWhen.AfterInteract && _showWhen != ControlsShowWhen.WhileInRange) { return; }
            Interactable interactable = GetComponent<Interactable>();
            if (interactable == null) { return; }
            interactable.OnHighlight?.AddListener(OnHighlighted);
            interactable.OnUnhighlight?.AddListener(OnUnhighlighted);
            interactable.OnLocalInteractEvent?.AddListener(OnInteracted);
        }

        private void OnDisable()
        {
            SetShown(false);
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
            SetShown(false);
        }

        private void OnHighlighted()
        {
            if (_showWhen == ControlsShowWhen.WhileInRange) { SetShown(true); }
        }

        private void OnUnhighlighted()
        {
            _interactShown = false;
            SetShown(false);
        }

        private void OnInteracted()
        {
            if (_showWhen != ControlsShowWhen.AfterInteract) { return; }
            _interactShown = !_interactShown;
            SetShown(_interactShown);
        }

        /// <summary>Show or hide this set. Called by the runtime/TestPlayer for seats and zones; idempotent.</summary>
        public void SetShown(bool shown)
        {
            bool has = _shown.Contains(this);
            if (shown == has) { return; }
            if (shown && !isActiveAndEnabled) { return; }
            if (shown) { _shown.Add(this); } else { _shown.Remove(this); }
            OnCurrentChanged?.Invoke();
        }

        /// <summary>The local player pressed button <paramref name="index"/>: runs On Pressed, then Everyone (via the router).</summary>
        public void Press(int index)
        {
            if (_controls == null || index < 0 || index >= _controls.Length || _controls[index] == null) { return; }
            if (_lastPressed.TryGetValue(index, out float last) && Time.time - last < _controls[index].cooldown) { return; }
            _lastPressed[index] = Time.time;
            _controls[index].onPressed?.Invoke();
            if (EveryoneRouter != null) { EveryoneRouter(this, index); }
            else { InvokeEveryone(index); }
        }

        public void InvokeEveryone(int index)
        {
            if (_controls == null || index < 0 || index >= _controls.Length || _controls[index] == null) { return; }
            _controls[index].onEveryone?.Invoke();
        }

        public float CooldownOf(int index)
        {
            if (_controls == null || index < 0 || index >= _controls.Length || _controls[index] == null) { return 0f; }
            return _controls[index].cooldown;
        }

        /// <summary>
        /// The WhileSeated controls for a seat: the seat Transform's own, else the owner's (a Seat object, or a
        /// Vehicle root covering every seat without its own — including the hidden default driver seat).
        /// </summary>
        public static CustomControls FindForSeat(Transform seat, Component owner)
        {
            CustomControls own = seat != null ? seat.GetComponent<CustomControls>() : null;
            if (own != null && own.ShowWhen == ControlsShowWhen.WhileSeated) { return own; }
            CustomControls shared = owner != null ? owner.GetComponent<CustomControls>() : null;
            return shared != null && shared.ShowWhen == ControlsShowWhen.WhileSeated ? shared : null;
        }

#if UNITY_EDITOR
        // Example zone (empty object + trigger box). Add buttons in the Inspector.
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Custom Controls Zone", isValidateFunction: false, priority: 0)]
        private static void CreateZone(UnityEditor.MenuCommand menuCommand)
        {
            GameObject go = new GameObject("CustomControlsZone");
            go.AddComponent<CustomControls>();
            go.transform.localScale = new Vector3(3f, 2f, 3f);
            UnityEditor.GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "New Custom Controls Zone");
            UnityEditor.Selection.activeObject = go;
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
