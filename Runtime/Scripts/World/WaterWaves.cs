using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Rolling waves on a water surface. Put it on the water's Renderer, over a Swim Zone. Boats ride these waves
    /// (height and tilt) in the app and with the TestPlayer. The water shows the same waves when its shader calls the
    /// SDK's <c>Runtime/Shaders/VVWaterWaves.hlsl</c>: this component hands the Renderer <c>_VVWaveA</c> and
    /// <c>_VVWaveB</c> every frame, and a water Renderer without it stays flat. Two sine swells: the main one along
    /// Direction, and a smaller, shorter one at an angle so the surface doesn't look combed.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("VirtualVenues/Water Waves")]
    public class WaterWaves : MonoBehaviour
    {
        private const float CrossAngle = 35f;
        private const float CrossLength = 0.57f;
        private const float CrossSpeed = 0.75f;
        private const float MainShare = 0.35f;   // amplitude = share × height; the two shares add up to half the height
        private const float CrossShare = 0.15f;
        private const double TwoPi = Math.PI * 2.0;

        [Tooltip("Crest to trough, metres. 0 = flat water.")]
        [Min(0f)] [SerializeField] private float _height = 0.4f;
        [Tooltip("Distance from one crest to the next, metres.")]
        [Min(1f)] [SerializeField] private float _length = 10f;
        [Tooltip("How fast the crests travel, metres per second.")]
        [Min(0f)] [SerializeField] private float _speed = 1.5f;
        [Tooltip("Compass direction the waves travel in, degrees: 0 = world +Z, 90 = world +X.")]
        [SerializeField] private float _direction = 45f;

        /// <summary>The wave clock, seconds. The app points it at the network clock, so every player sees the same crests.</summary>
        public static Func<double> Clock = () => Time.timeAsDouble;

        private static readonly int WaveA = Shader.PropertyToID("_VVWaveA");
        private static readonly int WaveB = Shader.PropertyToID("_VVWaveB");
        private static readonly List<WaterWaves> _instances = new List<WaterWaves>();

        private Renderer _renderer = null;
        private MaterialPropertyBlock _block = null;

        public float Height => _height;
        public float Length => _length;
        public float Speed => _speed;
        public float Direction => _direction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instances.Clear();
            Clock = () => Time.timeAsDouble;
        }

        private void OnEnable()
        {
            if (!_instances.Contains(this)) { _instances.Add(this); }
            _renderer = GetComponent<Renderer>();
            if (_renderer == null) { Debug.LogWarning($"[WaterWaves] '{name}' has no Renderer. Put Water Waves on the water surface.", this); }
            Push(Clock());
        }

        private void OnDisable()
        {
            _instances.Remove(this);
            if (_renderer == null || _block == null) { return; }
            _renderer.GetPropertyBlock(_block);
            _block.SetVector(WaveA, Vector4.zero);
            _block.SetVector(WaveB, Vector4.zero);
            _renderer.SetPropertyBlock(_block);
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) { Push(Clock()); }
        }

        private void LateUpdate()
        {
            Push(Clock());
        }

        private void Push(double time)
        {
            if (_renderer == null) { return; }
            if (_block == null) { _block = new MaterialPropertyBlock(); }
            GetWaves(time, out Vector4 a, out Vector4 b);
            _renderer.GetPropertyBlock(_block);
            _block.SetVector(WaveA, a);
            _block.SetVector(WaveB, b);
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>The two swells at <paramref name="time"/>: xy = wave vector (rad/m), z = amplitude (m), w = phase (rad).</summary>
        public void GetWaves(double time, out Vector4 a, out Vector4 b)
        {
            a = Swell(_direction, _length, _speed, MainShare * _height, time);
            b = Swell(_direction + CrossAngle, _length * CrossLength, _speed * CrossSpeed, CrossShare * _height, time);
        }

        private static Vector4 Swell(float direction, float length, float speed, float amplitude, double time)
        {
            double k = TwoPi / Math.Max(1f, length);
            double phase = (k * speed * time) % TwoPi; // in double: a long-running server clock stays smooth
            float rad = direction * Mathf.Deg2Rad;
            return new Vector4((float)k * Mathf.Sin(rad), (float)k * Mathf.Cos(rad), amplitude, (float)phase);
        }

        /// <summary>
        /// Height above the still water line at world (x, z), and the surface normal. The same formula as
        /// <c>VVWaterWaves.hlsl</c>: keep them identical, or boats bob out of step with the water.
        /// </summary>
        public static float Evaluate(Vector4 a, Vector4 b, float x, float z, out Vector3 normal)
        {
            float pa = a.x * x + a.y * z - a.w;
            float pb = b.x * x + b.y * z - b.w;
            float ca = a.z * Mathf.Cos(pa);
            float cb = b.z * Mathf.Cos(pb);
            normal = new Vector3(-(ca * a.x + cb * b.x), 1f, -(ca * a.y + cb * b.y)).normalized;
            return a.z * Mathf.Sin(pa) + b.z * Mathf.Sin(pb);
        }

        /// <summary>The waves at a world point now: height above still water, and the normal. 0 and up where no Water Waves covers it.</summary>
        public static float Sample(Vector3 point, out Vector3 normal)
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                WaterWaves waves = _instances[i];
                if (waves == null || waves._renderer == null) { continue; }
                Bounds b = waves._renderer.bounds;
                if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z) { continue; }
                waves.GetWaves(Clock(), out Vector4 a, out Vector4 c);
                return Evaluate(a, c, point.x, point.z, out normal);
            }
            normal = Vector3.up;
            return 0f;
        }
    }
}
