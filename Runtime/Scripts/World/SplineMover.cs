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
        [Tooltip("Seconds for one lap (PingPong: one way). Ignored when the track has a SplineRide (physics sets the lap time).")]
        [Min(0.1f)] [SerializeField] private float _duration = 20f;
        [Tooltip("Loop: go round and round. PingPong: go to the end and back (not on a SplineRide track). Once: ride to the end and stop.")]
        [SerializeField] private SplineLoopMode _loopMode = SplineLoopMode.Loop;
        [Tooltip("Where on the track the ride starts, 0-1. Spread several carts on one track with this. With a SplineRide it's a fraction of the lap time: give every car of one train the same value and use Gap Metres.")]
        [Range(0f, 1f)] [SerializeField] private float _startOffset = 0f;
        [Tooltip("With a SplineRide: metres behind the train's lead car (0 = the lead car).")]
        [Min(0f)] [SerializeField] private float _gapMetres = 0f;
        [Tooltip("With a SplineRide: this car fires the track events. Leave on for the lead car only.")]
        [SerializeField] private bool _fireEvents = true;
        [Tooltip("Turn to face along the track. Off = keep the authored rotation.")]
        [SerializeField] private bool _alignToSpline = true;
        [Tooltip("With Align: only turn left/right, never tilt with slopes or banking (boats, platforms). Off for coasters.")]
        [SerializeField] private bool _keepUpright = true;
        [Tooltip("Players standing on it move with it.")]
        [SerializeField] private bool _carryPlayers = true;

        private double _enableTime = 0d;
        private bool _warned = false;
        private bool _pingPongWarned = false;
        private SplineContainer _rideFor = null;
        private SplineRide _ride = null;
        // Last frame's ride position, for firing track events. Cleared on enable.
        private bool _hasPrev = false;
        private double _prevTime = 0d;
        private float _prevMetres = 0f;

        /// <summary>A clock jump bigger than this (seconds) repositions the cart without firing the events it skipped.</summary>
        public const double MaxEventStep = 2d;

        public SplineContainer Spline => _spline;
        public float Duration => _duration;
        public SplineLoopMode LoopMode => _loopMode;
        public float StartOffset => _startOffset;
        public float GapMetres => _gapMetres;
        public bool FireEvents => _fireEvents;
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
            _hasPrev = false;
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

            bool reverse = false;
            float t = _ride != null && _ride.isActiveAndEnabled && _ride.LapTime > 0d
                ? RideT(_ride, time)
                : EvaluateT(time, _duration, _startOffset, _loopMode, out reverse);
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

        // Physics ride: where the train is at this time, from the ride's baked time → metres table.
        private float RideT(SplineRide ride, double time)
        {
            double lap = ride.LapTime;
            double rideTime;
            if (_loopMode == SplineLoopMode.Once)
            {
                rideTime = Math.Min(Math.Max(time + _startOffset * lap, 0d), lap);
            }
            else
            {
                if (_loopMode == SplineLoopMode.PingPong && !_pingPongWarned)
                {
                    _pingPongWarned = true;
                    Debug.LogWarning($"SplineMover '{name}': PingPong isn't supported on a SplineRide track; looping instead.", this);
                }
                double laps = time / lap + _startOffset;
                rideTime = (laps - Math.Floor(laps)) * lap;
            }

            float metres = ride.MetresAt(rideTime) - _gapMetres;
            metres = ride.Closed && _loopMode != SplineLoopMode.Once ? Mathf.Repeat(metres, ride.Length) : Mathf.Max(0f, metres);

            if (_fireEvents)
            {
                // Resync on time, not distance: the local → network clock swap on connect jumps the train anywhere.
                bool resync = !_hasPrev || time < _prevTime || time - _prevTime > MaxEventStep;
                if (!resync) { ride.FirePassed(_prevMetres, metres, this); }
                _hasPrev = true;
                _prevTime = time;
                _prevMetres = metres;
            }
            return ride.ToNormalized(metres);
        }

        private SplineContainer ResolveSpline()
        {
            if (_spline == null) { _spline = GetComponentInParent<SplineContainer>(); }
            bool ownTrack = _spline != null && _spline.transform.IsChildOf(transform);
            if (_spline != _rideFor)
            {
                _rideFor = _spline;
                _ride = _spline != null ? _spline.GetComponent<SplineRide>() : null;
            }
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

        // Example coaster (Resources/SplineCoaster.prefab): a SplineTrack with carts that tilt with the rails.
        [UnityEditor.MenuItem("GameObject/VirtualVenues/New Roller Coaster", isValidateFunction: false, priority: 0)]
        private static void CreateRollerCoaster(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("SplineCoaster", Vector3.zero);
        }
#endif
    }
}
