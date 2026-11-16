using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public enum AudioZoneShape
    {
        Box,
        Sphere
    }

    /// <summary>
    /// A box or sphere that keeps the sound inside it apart from the outside. While the listener is inside, audio
    /// located outside the zone with a lower <see cref="Priority"/> than the zone is turned down to its own duck floor
    /// (Duck floor on its AudioChannel / Speaker); audio inside the zone is not touched. The change fades in over
    /// <see cref="EdgeBlend"/> metres from the zone's edge inwards. Part of audio priority (<see cref="AudioPriority"/>):
    /// higher-priority sources still duck lower ones as usual. No collider needed. A <see cref="RoomBoundary"/> still
    /// applies first (it cuts streams and voice between rooms completely). The app does the mixing at runtime.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Audio Occlusion Zone")]
    public class AudioOcclusionZone : MonoBehaviour
    {
        [Tooltip("Box or sphere, in this object's local space (it moves, turns and scales with the object).")]
        [SerializeField] private AudioZoneShape _shape = AudioZoneShape.Box;
        [Tooltip("Centre of the zone, local to this object.")]
        [SerializeField] private Vector3 _center = Vector3.zero;
        [Tooltip("Box size, local to this object.")]
        [SerializeField] private Vector3 _size = new Vector3(10f, 5f, 10f);
        [Tooltip("Sphere radius, local to this object (scaled by its largest axis).")]
        [SerializeField, Min(0f)] private float _radius = 5f;
        [Space]
        [Tooltip("Audio outside the zone with a lower priority than this is turned down while the listener is inside.")]
        [SerializeField] private int _priority = AudioPriority.DefaultZonePriority;
        [Tooltip("Metres from the edge inwards over which the outside audio fades down. 0 = a hard cut at the edge.")]
        [SerializeField, Min(0f)] private float _edgeBlend = AudioPriority.DefaultEdgeBlend;

        private static readonly List<AudioOcclusionZone> _instances = new List<AudioOcclusionZone>();
        /// <summary>The enabled zones.</summary>
        public static List<AudioOcclusionZone> Instances => _instances;
        public static Action<AudioOcclusionZone> onZoneAdded = null;
        public static Action<AudioOcclusionZone> onZoneRemoved = null;

        // Filled by Prepare(), so a tick reads the transform once per zone instead of once per source.
        private Matrix4x4 _worldToLocal = Matrix4x4.identity;
        private Vector3 _scale = Vector3.one;
        private Vector3 _worldCenter;

        public AudioZoneShape Shape { get => _shape; set { _shape = value; } }
        public Vector3 Center { get => _center; set { _center = value; } }
        public Vector3 Size { get => _size; set { _size = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z)); } }
        public float Radius { get => _radius; set { _radius = Mathf.Max(0f, value); } }
        public float EdgeBlend { get => _edgeBlend; set { _edgeBlend = Mathf.Max(0f, value); } }

        public int Priority
        {
            get => _priority;
            set { if (_priority == value) { return; } _priority = value; AudioPriority.onPriorityChanged?.Invoke(this); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instances.Clear();
            onZoneAdded = null;
            onZoneRemoved = null;
        }

        private void OnEnable()
        {
            if (_instances.Contains(this)) { return; }
            _instances.Add(this);
            onZoneAdded?.Invoke(this);
        }

        private void OnDisable()
        {
            if (_instances.Remove(this)) { onZoneRemoved?.Invoke(this); }
        }

        private void OnValidate()
        {
            _radius = Mathf.Max(0f, _radius);
            _edgeBlend = Mathf.Max(0f, _edgeBlend);
            _size = new Vector3(Mathf.Abs(_size.x), Mathf.Abs(_size.y), Mathf.Abs(_size.z));
            if (Application.isPlaying) { AudioPriority.onPriorityChanged?.Invoke(this); }
        }

        /// <summary>Reads the transform once; <see cref="DepthPrepared"/> then uses it. Call after the zone moved.</summary>
        public void Prepare()
        {
            Transform t = transform;
            _worldToLocal = t.worldToLocalMatrix;
            Vector3 s = t.lossyScale;
            _scale = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            _worldCenter = t.TransformPoint(_center);
        }

        /// <summary>Metres inside the zone (positive) or outside it (negative). Reads the transform.</summary>
        public float Depth(Vector3 worldPoint)
        {
            Prepare();
            return DepthPrepared(worldPoint);
        }

        /// <summary><see cref="Depth"/> with the transform read by the last <see cref="Prepare"/>.</summary>
        public float DepthPrepared(Vector3 worldPoint)
        {
            if (_shape == AudioZoneShape.Sphere)
            {
                float r = _radius * Mathf.Max(_scale.x, Mathf.Max(_scale.y, _scale.z));
                return r - (worldPoint - _worldCenter).magnitude;
            }
            Vector3 local = _worldToLocal.MultiplyPoint3x4(worldPoint) - _center;
            float dx = (Mathf.Abs(local.x) - _size.x * 0.5f) * _scale.x;
            float dy = (Mathf.Abs(local.y) - _size.y * 0.5f) * _scale.y;
            float dz = (Mathf.Abs(local.z) - _size.z * 0.5f) * _scale.z;
            if (dx <= 0f && dy <= 0f && dz <= 0f) { return -Mathf.Max(dx, Mathf.Max(dy, dz)); }
            float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f), oz = Mathf.Max(dz, 0f);
            return -Mathf.Sqrt(ox * ox + oy * oy + oz * oz);
        }

        /// <summary>True when the point is strictly inside the zone (after <see cref="Prepare"/>).</summary>
        public bool ContainsPrepared(Vector3 worldPoint) => DepthPrepared(worldPoint) > 0f;

        /// <summary>
        /// 0..1, how strongly the zone applies for a listener here (after <see cref="Prepare"/>): 0 outside or on the edge,
        /// rising to 1 at <see cref="EdgeBlend"/> metres inside.
        /// </summary>
        public float ListenerPresencePrepared(Vector3 listener) => Presence(DepthPrepared(listener), _edgeBlend);

        /// <summary>The presence for a listener <paramref name="depth"/> metres inside a zone with this edge blend.</summary>
        public static float Presence(float depth, float edgeBlend)
        {
            if (depth <= 0f) { return 0f; }
            if (edgeBlend <= 0f || depth >= edgeBlend) { return 1f; }
            return depth / edgeBlend;
        }

#if UNITY_EDITOR
        private static readonly Color ZoneColor = new Color(0.55f, 0.35f, 1f);

        private void OnDrawGizmos() { DrawZone(UnityEditor.Selection.Contains(gameObject) ? 0.12f : 0.04f); }

        private void DrawZone(float fill)
        {
            Matrix4x4 old = Gizmos.matrix;
            if (_shape == AudioZoneShape.Sphere)
            {
                Transform t = transform;
                Vector3 s = t.lossyScale;
                float r = _radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                Vector3 c = t.TransformPoint(_center);
                Gizmos.color = new Color(ZoneColor.r, ZoneColor.g, ZoneColor.b, fill);
                Gizmos.DrawSphere(c, r);
                Gizmos.color = ZoneColor;
                Gizmos.DrawWireSphere(c, r);
                if (_edgeBlend > 0f && r - _edgeBlend > 0f)
                {
                    Gizmos.color = new Color(ZoneColor.r, ZoneColor.g, ZoneColor.b, 0.45f);
                    Gizmos.DrawWireSphere(c, r - _edgeBlend);
                }
                return;
            }
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(ZoneColor.r, ZoneColor.g, ZoneColor.b, fill);
            Gizmos.DrawCube(_center, _size);
            Gizmos.color = ZoneColor;
            Gizmos.DrawWireCube(_center, _size);
            if (_edgeBlend > 0f)
            {
                // Inner outline: where the outside audio reaches its floor (edge blend in metres, so undo the scale).
                Vector3 s = transform.lossyScale;
                Vector3 inner = new Vector3(
                    _size.x - 2f * _edgeBlend / Mathf.Max(Mathf.Abs(s.x), 0.0001f),
                    _size.y - 2f * _edgeBlend / Mathf.Max(Mathf.Abs(s.y), 0.0001f),
                    _size.z - 2f * _edgeBlend / Mathf.Max(Mathf.Abs(s.z), 0.0001f));
                if (inner.x > 0f && inner.y > 0f && inner.z > 0f)
                {
                    Gizmos.color = new Color(ZoneColor.r, ZoneColor.g, ZoneColor.b, 0.45f);
                    Gizmos.DrawWireCube(_center, inner);
                }
            }
            Gizmos.matrix = old;
        }
#endif
    }
}
