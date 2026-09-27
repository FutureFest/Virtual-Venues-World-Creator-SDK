using System;
using UnityEngine;
using UnityEngine.Splines;

namespace VirtualVenues.WorldCreator
{
    /// <summary>How a <see cref="SplineMover"/> travels its spline. Append only.</summary>
    public enum SplineLoopMode { Loop, PingPong, Once }

    /// <summary>
    /// Moves this object along a spline at constant speed: river boats, coaster carts, moving platforms.
    /// Riders = <see cref="Seat"/> markers on children. Loop and PingPong are a pure function of the clock, so
    /// every player sees the ride in the same place (the core project swaps in the shared network clock).
    /// Put this on a CHILD of the spline object, never on the spline itself (it would drag its own track).
    /// </summary>
    // Movers run before KeepUpright (-40) and the player's platform probe (0), so a rider reads this frame's pose.
    [DefaultExecutionOrder(-50)]
    public class SplineMover : MonoBehaviour
    {
        private static InstanceTracker<SplineMover> _tracker = new InstanceTracker<SplineMover>();
        public static InstanceTracker<SplineMover> Tracker => _tracker;

        /// <summary>The clock rides run on. Local time by default; the core project sets the network clock.</summary>
        public static Func<double> Clock = null;

        [Tooltip("The track. Empty = the nearest SplineContainer above this object.")]
        [SerializeField] private SplineContainer _spline = null;
        [Tooltip("Seconds for one lap (PingPong: one way).")]
        [Min(0.1f)] [SerializeField] private float _duration = 20f;
        [SerializeField] private SplineLoopMode _loopMode = SplineLoopMode.Loop;
        [Tooltip("Where on the track the ride starts, 0-1. Spread several carts on one track with this.")]
        [Range(0f, 1f)] [SerializeField] private float _startOffset = 0f;
        [Tooltip("Turn to face along the track. Off = keep the authored rotation.")]
        [SerializeField] private bool _alignToSpline = true;
        [Tooltip("With Align: only turn left/right, never tilt with slopes or banking (boats, platforms).")]
        [SerializeField] private bool _keepUpright = true;
        [Tooltip("Players standing on it move with it.")]
        [SerializeField] private bool _carryPlayers = true;

        private double _enableTime = 0d;
        private bool _warned = false;

        public SplineContainer Spline => _spline;
        public float Duration => _duration;
        public SplineLoopMode LoopMode => _loopMode;
        public float StartOffset => _startOffset;
        public bool AlignToSpline => _alignToSpline;
        public bool KeepUpright => _keepUpright;
        public bool CarryPlayers => _carryPlayers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracker.Clear();
            Clock = null;
        }

        private static double Now => Clock != null ? Clock() : Time.timeAsDouble;

        private void Awake()
        {
            _tracker.AddInstance(this);
        }

        private void OnEnable()
        {
            // Once is local-only, so it keeps local time: the shared Clock can swap domains mid-ride (on connect).
            _enableTime = Time.timeAsDouble;
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
        }

        private void Update()
        {
            // Once can't run off the shared clock (it would finish before anyone joined): it plays from when it appeared.
            Apply(_loopMode == SplineLoopMode.Once ? Time.timeAsDouble - _enableTime : Now);
        }

        /// <summary>
        /// 0-1 position on the track at <paramref name="time"/> seconds. <paramref name="reverse"/> is true on
        /// the PingPong return leg.
        /// </summary>
        public static float EvaluateT(double time, float duration, float startOffset, SplineLoopMode mode, out bool reverse)
        {
            reverse = false;
            double laps = time / Math.Max(0.1, duration) + startOffset;
            switch (mode)
            {
                case SplineLoopMode.PingPong:
                    double p = laps - 2d * Math.Floor(laps / 2d);
                    reverse = p > 1d;
                    return (float)(reverse ? 2d - p : p);
                case SplineLoopMode.Once:
                    return Mathf.Clamp01((float)laps);
                default:
                    return (float)(laps - Math.Floor(laps));
            }
        }

        /// <summary>Places the object where the ride is at <paramref name="time"/>. Absolute, not integrated.</summary>
        public void Apply(double time)
        {
            SplineContainer spline = ResolveSpline();
            if (spline == null) { return; }

            float t = EvaluateT(time, _duration, _startOffset, _loopMode, out bool reverse);
            if (!spline.Evaluate(t, out var position, out var tangent, out var up)) { return; }

            Vector3 forward = (Vector3)tangent;
            if (reverse) { forward = -forward; }
            if (_alignToSpline)
            {
                Vector3 upDir = _keepUpright ? Vector3.up : (Vector3)up;
                if (_keepUpright) { forward.y = 0f; }
                if (forward.sqrMagnitude > 1e-6f) { transform.rotation = Quaternion.LookRotation(forward, upDir); }
            }
            transform.position = position;
        }

        private SplineContainer ResolveSpline()
        {
            if (_spline == null) { _spline = GetComponentInParent<SplineContainer>(); }
            bool ownTrack = _spline != null && _spline.transform.IsChildOf(transform);
            if (_spline != null && !ownTrack && _spline.Spline != null && _spline.Spline.Count > 1) { return _spline; }

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning(ownTrack
                    ? $"SplineMover '{name}' is on (or above) its own spline and would drag its track. Put it on a child."
                    : $"SplineMover '{name}' has no spline with at least 2 knots.", this);
            }
            return null;
        }

#if UNITY_EDITOR
        // Example ride (Resources/SplineRide.prefab): a loop track with a platform child that carries players.
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Spline Ride", isValidateFunction: false, priority: 0)]
        private static void CreateSplineRide(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("SplineRide", Vector3.zero);
        }
#endif
    }
}
