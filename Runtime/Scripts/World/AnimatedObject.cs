using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Ping-pongs this object's local position, rotation (euler) or scale between <see cref="From"/> and
    /// <see cref="To"/>, one leg every <see cref="Duration"/> seconds. Sky balloons, bobbing props, swinging signs.
    /// Previews on local time in any project; the core project swaps <see cref="TimeSource"/> for network time so
    /// every client shows the same pose. <see cref="CarryPlayers"/> lets players ride it (core adds the platform).
    /// For a continuous spin use <see cref="RotatingObject"/>.
    /// </summary>
    public class AnimatedObject : MonoBehaviour
    {
        public enum AnimationMode { Move, Rotate, Scale } // append only

        private static InstanceTracker<AnimatedObject> _tracker = new InstanceTracker<AnimatedObject>();
        public static InstanceTracker<AnimatedObject> Tracker => _tracker;

        /// <summary>Shared clock in seconds. Null = local time. Set by the core project.</summary>
        public static Func<double> TimeSource = null;

        /// <summary>Raised on every enable (the core attaches the player carrier then, with final settings).</summary>
        public static event Action<AnimatedObject> OnEnabled;

        [SerializeField] private AnimationMode _mode = AnimationMode.Move;
        [Tooltip("Local position / euler angles / scale at one end of the swing.")]
        [SerializeField] private Vector3 _from = Vector3.zero;
        [Tooltip("Local position / euler angles / scale at the other end of the swing.")]
        [SerializeField] private Vector3 _to = Vector3.up;
        [Tooltip("Seconds to go from one end to the other.")]
        [SerializeField, Min(0.01f)] private float _duration = 2f;
        [Tooltip("Slow in and out at each end.")]
        [SerializeField] private bool _easing = true;
        [Tooltip("Players standing on it move with it (needs a non-trigger collider).")]
        [SerializeField] private bool _carryPlayers = false;

        public AnimationMode Mode => _mode;
        public Vector3 From => _from;
        public Vector3 To => _to;
        public float Duration => _duration;
        public bool Easing => _easing;
        public bool CarryPlayers => _carryPlayers;

        // Pose when enabled; put back on disable so a stopped animation leaves the object where it was placed.
        private Vector3 _restPosition;
        private Vector3 _restEuler;
        private Vector3 _restScale;
        private bool _hasRest = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracker.Clear();
            TimeSource = null;
            OnEnabled = null;
        }

        private void Reset()
        {
            _from = transform.localPosition;
            _to = transform.localPosition + Vector3.up;
        }

        private void Awake()
        {
            _tracker.AddInstance(this);
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
        }

        private void OnEnable()
        {
            _restPosition = transform.localPosition;
            _restEuler = transform.localEulerAngles;
            _restScale = transform.localScale;
            _hasRest = true;
            OnEnabled?.Invoke(this);
        }

        private void OnDisable()
        {
            if (!_hasRest) { return; }
            transform.localPosition = _restPosition;
            transform.localEulerAngles = _restEuler;
            transform.localScale = _restScale;
        }

        /// <summary>Set from external data (a loaded world layout). Values as the serialized fields.</summary>
        public void Configure(AnimationMode mode, Vector3 from, Vector3 to, float duration, bool easing, bool carryPlayers)
        {
            _mode = mode;
            _from = from;
            _to = to;
            _duration = Mathf.Max(0.01f, duration);
            _easing = easing;
            _carryPlayers = carryPlayers;
        }

        /// <summary>The resting local value for <paramref name="mode"/>: the pose captured on enable while animating, else the current one.</summary>
        public Vector3 RestValue(AnimationMode mode)
        {
            bool live = _hasRest && isActiveAndEnabled;
            switch (mode)
            {
                case AnimationMode.Rotate: return live ? _restEuler : transform.localEulerAngles;
                case AnimationMode.Scale: return live ? _restScale : transform.localScale;
                default: return live ? _restPosition : transform.localPosition;
            }
        }

        private void Update()
        {
            ApplyAt(TimeSource != null ? TimeSource() : Time.timeAsDouble);
        }

        /// <summary>Pose the object for absolute time <paramref name="time"/> (seconds).</summary>
        public void ApplyAt(double time)
        {
            Vector3 value = Vector3.LerpUnclamped(_from, _to, Phase(time, _duration, _easing));
            switch (_mode)
            {
                case AnimationMode.Move: transform.localPosition = value; break;
                case AnimationMode.Rotate: transform.localEulerAngles = value; break;
                case AnimationMode.Scale: transform.localScale = value; break;
            }
        }

        /// <summary>
        /// 0 → 1 → 0 ping-pong, one leg per <paramref name="duration"/> seconds. Folded in double so hours of
        /// network time don't jitter a short bob.
        /// </summary>
        public static float Phase(double time, float duration, bool easing)
        {
            if (duration <= 0f) { return 0f; }
            double f = (time / duration) % 2.0;
            if (f < 0.0) { f += 2.0; }
            float t = (float)(f <= 1.0 ? f : 2.0 - f);
            return easing ? Mathf.SmoothStep(0f, 1f, t) : t;
        }

#if UNITY_EDITOR
        // Example bobbing cube (Resources/AnimatedObject.prefab).
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Animated Object", isValidateFunction: false, priority: 0)]
        private static void CreateAnimatedObject(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("AnimatedObject", Vector3.zero);
        }
#endif
    }
}
