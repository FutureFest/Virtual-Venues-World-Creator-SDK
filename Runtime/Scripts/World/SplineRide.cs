using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.Splines;

namespace VirtualVenues.WorldCreator
{
    /// <summary>What a <see cref="TrackSection"/> does to the cart. Append only.</summary>
    public enum TrackSectionType { Lift, Booster, Brake, Station }

    /// <summary>A stretch of track with its own drive: chain lift, magnetic booster, brake run or station stop.</summary>
    [Serializable]
    public class TrackSection
    {
        [Tooltip("Lift: a chain pulls the train uphill at Speed.\nBooster: magnets push the train up to Speed.\nBrake: slows the train down to Speed.\nStation: stops the train at Start, waits Hold Seconds, then launches it to Speed.")]
        [FormerlySerializedAs("_kind")] [SerializeField] private TrackSectionType _type = TrackSectionType.Lift;
        [Tooltip("Metres along the track where the section begins.")]
        [Min(0f)] [SerializeField] private float _start = 0f;
        [Tooltip("Metres along the track where the section ends.")]
        [Min(0f)] [SerializeField] private float _end = 10f;
        [Tooltip("Lift: chain speed. Booster: speed it pushes up to. Brake: speed it slows down to. Station: launch speed.")]
        [Min(0f)] [SerializeField] private float _speed = 3f;
        [Tooltip("Booster / Brake / Station: how hard it pushes or brakes (m/s²).")]
        [Min(0.01f)] [SerializeField] private float _force = 4f;
        [Tooltip("Station: seconds the train waits at the start of the section.")]
        [Min(0f)] [SerializeField] private float _holdSeconds = 5f;

        public TrackSectionType Type => _type;
        public float Start => _start;
        public float End => _end;
        public float Speed => _speed;
        public float Force => _force;
        public float HoldSeconds => _holdSeconds;

        public TrackSection() { }

        public TrackSection(TrackSectionType type, float start, float end, float speed, float force = 4f, float holdSeconds = 5f)
        {
            _type = type;
            _start = start;
            _end = end;
            _speed = speed;
            _force = force;
            _holdSeconds = holdSeconds;
        }

        public bool Contains(float metres) => metres >= _start && metres < _end;
    }

    /// <summary>A point on the track that fires actions when the train passes it.</summary>
    [Serializable]
    public class TrackEvent
    {
        [Tooltip("A label for this event, shown on the track in the Scene view.")]
        [SerializeField] private string _name = "Event";
        [Tooltip("Metres along the track.")]
        [Min(0f)] [SerializeField] private float _distance = 0f;
        [Tooltip("Optional: an Animator to trigger when the train passes.")]
        [SerializeField] private Animator _animator = null;
        [Tooltip("Trigger parameter set on the Animator.")]
        [SerializeField] private string _trigger = string.Empty;
        [Tooltip("Actions to run when the train passes: play a sound, burst particles, open a gate. Runs on every player's screen at the same moment.")]
        [SerializeField] private UnityEvent _onPass = new UnityEvent();

        public string Name => _name;
        public float Distance => _distance;
        public Animator Animator => _animator;
        public string Trigger => _trigger;
        public UnityEvent OnPass => _onPass;

        public TrackEvent() { }

        public TrackEvent(string name, float distance)
        {
            _name = name;
            _distance = distance;
        }

        public void Invoke()
        {
            // Called directly (not a persistent call) so IL2CPP stripping can't drop SetTrigger.
            if (_animator != null && !string.IsNullOrEmpty(_trigger)) { _animator.SetTrigger(_trigger); }
            _onPass?.Invoke();
        }
    }

    /// <summary>
    /// Ride physics for the <see cref="SplineMover"/> carts on this track: gravity, lift chains, boosters, brakes,
    /// station stops, plus <see cref="TrackEvent"/>s that fire as the train passes.
    /// The whole lap is simulated once into a time → distance table, and carts read it off the shared clock, so
    /// every player sees the train in the same place with no network traffic. Carts can't be pushed or roll back.
    /// Moving or rotating the track at runtime isn't supported (slopes are baked in world space on enable).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SplineContainer))]
    public class SplineRide : MonoBehaviour
    {
        public const float SampleStep = 0.25f;
        public const float MinSpeed = 0.5f;
        private const float HardStationArrival = 3f;

        /// <summary>Raised after a <see cref="TrackEvent"/> fires: ride, event index, the cart that passed it.</summary>
        public static event Action<SplineRide, int, SplineMover> OnEventPassed;

        [Tooltip("On: carts speed up downhill and slow down uphill. Off: carts keep their speed except in sections.")]
        [SerializeField] private bool _useGravity = true;
        [Tooltip("Starting speed (m/s) when the track has no Station or Lift.")]
        [Min(MinSpeed)] [SerializeField] private float _cruiseSpeed = 8f;
        [Tooltip("How hard gravity pulls (m/s²). 9.81 = Earth. Lower = floatier hills.")]
        [Min(0f)] [SerializeField] private float _gravity = 9.81f;
        [Tooltip("Rolling friction, as a fraction of gravity.")]
        [Min(0f)] [SerializeField] private float _friction = 0.02f;
        [Tooltip("Air drag, times speed squared.")]
        [Min(0f)] [SerializeField] private float _drag = 0.001f;
        [Tooltip("Stretches of track with their own drive: lift chains, boosters, brakes and station stops. Drawn in colour on the track.")]
        [SerializeField] private List<TrackSection> _sections = new List<TrackSection>();
        [Tooltip("Points on the track that fire actions when the train passes. Drawn as pink dots on the track.")]
        [SerializeField] private List<TrackEvent> _events = new List<TrackEvent>();

        private readonly List<double> _times = new List<double>();
        private readonly List<float> _metres = new List<float>();
        private readonly List<float> _speeds = new List<float>();
        private readonly List<string> _warnings = new List<string>();
        private float _length = 0f;
        private bool _closed = false;
        private float _topSpeed = 0f;
        private SplineContainer _container = null;

        public bool UseGravity => _useGravity;
        public float CruiseSpeed => _cruiseSpeed;
        public List<TrackSection> Sections => _sections;
        public List<TrackEvent> Events => _events;
        public IReadOnlyList<string> Warnings => _warnings;
        public float Length { get { EnsureBaked(); return _length; } }
        public bool Closed { get { EnsureBaked(); return _closed; } }
        public double LapTime { get { EnsureBaked(); return _times.Count > 0 ? _times[_times.Count - 1] : 0d; } }
        public float TopSpeed { get { EnsureBaked(); return _topSpeed; } }
        private SplineContainer Container => _container != null ? _container : _container = GetComponent<SplineContainer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnEventPassed = null;
        }

        private void OnEnable()
        {
            Rebuild();
#if UNITY_EDITOR
            UnityEngine.Splines.Spline.Changed += OnSplineChanged;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEngine.Splines.Spline.Changed -= OnSplineChanged;
#endif
        }

#if UNITY_EDITOR
        private void OnSplineChanged(UnityEngine.Splines.Spline spline, int knot, SplineModification modification)
        {
            if (Container != null && spline == Container.Spline) { Rebuild(); }
        }

        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) { Rebuild(); } };
        }
#endif

        /// <summary>Set from code or external data, then rebakes.</summary>
        public void Configure(bool useGravity, float cruiseSpeed, float friction, float drag)
        {
            _useGravity = useGravity;
            _cruiseSpeed = Mathf.Max(MinSpeed, cruiseSpeed);
            _friction = Mathf.Max(0f, friction);
            _drag = Mathf.Max(0f, drag);
            Rebuild();
        }

        private void EnsureBaked()
        {
            if (_times.Count == 0) { Rebuild(); }
        }

        /// <summary>Re-simulates the lap. Call after changing sections from code.</summary>
        public void Rebuild()
        {
            _times.Clear();
            _metres.Clear();
            _speeds.Clear();
            _warnings.Clear();
            _topSpeed = 0f;
            _length = 0f;

            SplineContainer container = Container;
            UnityEngine.Splines.Spline spline = container != null ? container.Spline : null;
            if (spline == null || spline.Count < 2) { return; }
            _length = spline.GetLength();
            _closed = spline.Closed;
            if (_length < 1e-3f) { return; }

            int n = Mathf.Max(2, Mathf.CeilToInt(_length / SampleStep));
            float h = _length / n;
            // World positions so a rotated or scaled track still gets its real slopes.
            var points = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = spline.ConvertIndexUnit(h * i, PathIndexUnit.Distance, PathIndexUnit.Normalized);
                points[i] = (Vector3)container.EvaluatePosition(t);
            }

            // A closed lap starts at the first Station (else Lift): it fixes the speed after it, so every lap is identical.
            TrackSection origin = _closed ? FirstSection(TrackSectionType.Station) ?? FirstSection(TrackSectionType.Lift) : null;
            int i0 = origin != null ? Mathf.Clamp(Mathf.CeilToInt(origin.Start / h - 1e-4f), 0, n - 1) : 0;
            float v = origin == null ? _cruiseSpeed : origin.Type == TrackSectionType.Lift ? Mathf.Max(MinSpeed, origin.Speed) : 0f;
            if (_closed && origin == null && _useGravity && _sections.Count > 0)
            {
                _warnings.Add("Closed track with no Station or Lift: the speed may jump where the lap wraps. Add a Station.");
            }

            double time = 0d;
            float startMetres = i0 * h;
            var stopped = new bool[_sections.Count];
            bool stallWarned = false;
            Add(time, startMetres, v);

            for (int k = 0; k < n; k++)
            {
                int a = (i0 + k) % n;
                float s = a * h;
                float sAbs = startMetres + k * h;

                // Station: stop at its start, wait, then launch.
                for (int j = 0; j < _sections.Count; j++)
                {
                    TrackSection st = _sections[j];
                    if (stopped[j] || st.Type != TrackSectionType.Station || s < st.Start - 1e-3f || s >= st.End) { continue; }
                    stopped[j] = true;
                    if (v > HardStationArrival) { _warnings.Add($"Train reaches the Station at {st.Start:0}m at {v:0.0} m/s. Add a Brake before it."); }
                    v = 0f;
                    if (k > 0) { Add(time, sAbs, 0f); }
                    time += st.HoldSeconds;
                    Add(time, sAbs, 0f);
                }

                Vector3 p0 = points[a];
                Vector3 p1 = points[a + 1];
                float ds = Mathf.Max(1e-4f, Vector3.Distance(p0, p1));
                float dy = p1.y - p0.y;
                TrackSection section = SectionAt(s + h * 0.5f);
                TrackSectionType kind = section != null ? section.Type : (TrackSectionType)(-1);

                float v1 = v;
                if (_useGravity && kind != TrackSectionType.Station)
                {
                    float v2 = v * v + 2f * (-_gravity * dy - (_friction * _gravity + _drag * v * v) * ds);
                    v1 = Mathf.Sqrt(Mathf.Max(0f, v2));
                }
                if (section != null)
                {
                    float target = section.Speed;
                    switch (kind)
                    {
                        case TrackSectionType.Lift:
                            v1 = Mathf.Max(v1, Mathf.Max(MinSpeed, target));
                            break;
                        case TrackSectionType.Booster:
                            if (v1 < target) { v1 = Mathf.Min(target, Mathf.Sqrt(v1 * v1 + 2f * section.Force * ds)); }
                            break;
                        case TrackSectionType.Brake:
                            if (v1 > target) { v1 = Mathf.Max(target, Mathf.Sqrt(Mathf.Max(0f, v1 * v1 - 2f * section.Force * ds))); }
                            break;
                        case TrackSectionType.Station:
                            target = Mathf.Max(MinSpeed, target);
                            v1 = v1 < target
                                ? Mathf.Min(target, Mathf.Sqrt(v1 * v1 + 2f * section.Force * ds))
                                : Mathf.Max(target, Mathf.Sqrt(Mathf.Max(0f, v1 * v1 - 2f * section.Force * ds)));
                            break;
                    }
                }
                if (v1 < MinSpeed && kind != TrackSectionType.Station)
                {
                    if (!stallWarned && kind != TrackSectionType.Brake)
                    {
                        stallWarned = true;
                        _warnings.Add($"Train stalls at {s:0}m (too slow to climb). Add a Lift or Booster before it.");
                    }
                    v1 = MinSpeed;
                }

                time += 2d * ds / Math.Max(1e-3d, v + v1);
                v = v1;
                Add(time, sAbs + h, v);
            }

            // The lap ends back at the origin Station, where the next lap stops the train.
            if (origin != null && origin.Type == TrackSectionType.Station && v > HardStationArrival)
            {
                _warnings.Add($"Train reaches the Station at {origin.Start:0}m at {v:0.0} m/s. Add a Brake before it.");
            }

            if (Application.isPlaying)
            {
                foreach (string w in _warnings) { Debug.LogWarning($"SplineRide '{name}': {w}", this); }
            }
        }

        private void Add(double time, float metres, float speed)
        {
            _times.Add(time);
            _metres.Add(metres);
            _speeds.Add(speed);
            _topSpeed = Mathf.Max(_topSpeed, speed);
        }

        private TrackSection FirstSection(TrackSectionType kind)
        {
            TrackSection best = null;
            foreach (TrackSection s in _sections)
            {
                if (s.Type == kind && s.End > s.Start && (best == null || s.Start < best.Start)) { best = s; }
            }
            return best;
        }

        /// <summary>The section covering <paramref name="metres"/> (first listed wins), or null.</summary>
        public TrackSection SectionAt(float metres)
        {
            foreach (TrackSection s in _sections)
            {
                if (s.Contains(metres)) { return s; }
            }
            return null;
        }

        /// <summary>Metres along the track (0..Length) at <paramref name="lapTime"/> seconds into the lap.</summary>
        public float MetresAt(double lapTime)
        {
            EnsureBaked();
            if (_times.Count == 0) { return 0f; }
            float m = Lookup(_times, lapTime, _metres);
            return _closed ? Mathf.Repeat(m, _length) : Mathf.Clamp(m, 0f, _length);
        }

        /// <summary>Simulated speed (m/s) at <paramref name="lapTime"/> seconds into the lap.</summary>
        public float SpeedAt(double lapTime)
        {
            EnsureBaked();
            return _times.Count == 0 ? 0f : Lookup(_times, lapTime, _speeds);
        }

        private static float Lookup(List<double> times, double time, List<float> values)
        {
            if (time <= times[0]) { return values[0]; }
            int last = times.Count - 1;
            if (time >= times[last]) { return values[last]; }
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (times[mid] <= time) { lo = mid; } else { hi = mid; }
            }
            double span = times[hi] - times[lo];
            float f = span > 0d ? (float)((time - times[lo]) / span) : 1f;
            return Mathf.Lerp(values[lo], values[hi], f);
        }

        /// <summary>Metres along the track → the 0-1 spline position the mover evaluates.</summary>
        public float ToNormalized(float metres)
        {
            SplineContainer container = Container;
            return container != null ? container.Spline.ConvertIndexUnit(metres, PathIndexUnit.Distance, PathIndexUnit.Normalized) : 0f;
        }

        /// <summary>Fires every event passed when a cart moved forward from <paramref name="from"/> to <paramref name="to"/> metres.</summary>
        public void FirePassed(float from, float to, SplineMover mover)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                if (!Passed(from, to, _events[i].Distance, Length, Closed)) { continue; }
                try { _events[i].Invoke(); }
                catch (Exception e) { Debug.LogException(e, this); }
                OnEventPassed?.Invoke(this, i, mover);
            }
        }

        /// <summary>True when moving forward from <paramref name="from"/> to <paramref name="to"/> crosses <paramref name="at"/>. Wraps on a closed track.</summary>
        public static bool Passed(float from, float to, float at, float length, bool closed)
        {
            float delta = to - from;
            if (closed && delta < -length * 0.5f) { delta += length; }
            if (delta <= 0f) { return false; }
            float rel = closed ? Mathf.Repeat(at - from, length) : at - from;
            return rel > 0f && rel <= delta;
        }

#if UNITY_EDITOR
        private static Color SectionColor(TrackSectionType type)
        {
            switch (type)
            {
                case TrackSectionType.Lift: return new Color(1f, 0.85f, 0.1f);
                case TrackSectionType.Booster: return new Color(0.2f, 0.6f, 1f);
                case TrackSectionType.Brake: return new Color(1f, 0.25f, 0.2f);
                default: return new Color(0.3f, 1f, 0.4f);
            }
        }

        /// <summary>World position at <paramref name="metres"/> along the track.</summary>
        public Vector3 PositionAt(float metres)
        {
            SplineContainer container = Container;
            if (container == null || container.Spline == null || container.Spline.Count < 2) { return transform.position; }
            return (Vector3)container.EvaluatePosition(ToNormalized(metres));
        }

        /// <summary>Metres along the track of the point nearest <paramref name="world"/>.</summary>
        public float NearestMetres(Vector3 world)
        {
            SplineContainer container = Container;
            if (container == null || container.Spline == null || container.Spline.Count < 2) { return 0f; }
            Vector3 local = container.transform.InverseTransformPoint(world);
            SplineUtility.GetNearestPoint(container.Spline, (Unity.Mathematics.float3)local, out _, out float t);
            return container.Spline.ConvertIndexUnit(t, PathIndexUnit.Normalized, PathIndexUnit.Distance);
        }

        private void OnDrawGizmos()
        {
            SplineContainer container = Container;
            if (container == null || container.Spline == null || container.Spline.Count < 2) { return; }
            Vector3 lift = transform.up * 0.3f;
            foreach (TrackSection s in _sections)
            {
                if (s.End <= s.Start) { continue; }
                int steps = Mathf.Max(2, Mathf.CeilToInt((s.End - s.Start) / 1f));
                var line = new Vector3[steps + 1];
                for (int i = 0; i <= steps; i++) { line[i] = PositionAt(Mathf.Lerp(s.Start, s.End, (float)i / steps)) + lift; }
                UnityEditor.Handles.color = SectionColor(s.Type);
                UnityEditor.Handles.DrawAAPolyLine(8f, line);
                UnityEditor.Handles.Label(line[0] + lift, s.Type.ToString());
            }
            Gizmos.color = Color.magenta;
            foreach (TrackEvent e in _events)
            {
                Vector3 p = PositionAt(e.Distance) + lift;
                Gizmos.DrawSphere(p, 0.35f);
                UnityEditor.Handles.Label(p + lift, e.Name);
            }
        }
#endif
    }
}
