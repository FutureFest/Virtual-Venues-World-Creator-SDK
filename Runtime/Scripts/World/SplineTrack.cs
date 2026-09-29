using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Builds coaster rails + ties along the spline on this object, so <see cref="SplineMover"/> carts ride a
    /// visible track. Uses the same spline frame as the mover (turn the carts' Keep Upright off to follow it).
    /// The mesh is rebuilt on load and never saved.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
    public class SplineTrack : MonoBehaviour
    {
        [Tooltip("Distance between the two rails, centre to centre (metres).")]
        [Min(0.05f)] [SerializeField] private float _gauge = 1.2f;
        [Min(0.005f)] [SerializeField] private float _railRadius = 0.06f;
        [Range(3, 16)] [SerializeField] private int _railSides = 6;
        [Tooltip("Rail sample spacing along the track (metres). Smaller = smoother curves, more triangles.")]
        [Min(0.05f)] [SerializeField] private float _step = 0.5f;
        [Tooltip("Metres between ties. 0 = no ties.")]
        [Min(0f)] [SerializeField] private float _tieSpacing = 1f;
        [SerializeField] private Vector3 _tieSize = new Vector3(1.5f, 0.08f, 0.2f);
        [Tooltip("Moves the rails along the track's up (metres). Negative = below the spline, to meet carts whose wheels sit below their pivot.")]
        [SerializeField] private float _heightOffset = 0f;

        private Mesh _mesh = null;

        public float Gauge => _gauge;
        public float RailRadius => _railRadius;
        public int RailSides => _railSides;
        public float TieSpacing => _tieSpacing;
        public float HeightOffset => _heightOffset;
        public Mesh TrackMesh => _mesh;

        private void OnEnable()
        {
            Rebuild();
#if UNITY_EDITOR
            Spline.Changed += OnSplineChanged;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            Spline.Changed -= OnSplineChanged;
#endif
        }

        private void OnDestroy()
        {
            if (_mesh == null) { return; }
            if (Application.isPlaying) { Destroy(_mesh); } else { DestroyImmediate(_mesh); }
        }

#if UNITY_EDITOR
        private void OnSplineChanged(Spline spline, int knot, SplineModification modification)
        {
            SplineContainer container = GetComponent<SplineContainer>();
            if (container != null && spline == container.Spline) { Rebuild(); }
        }

        private void OnValidate()
        {
            // Rebuilding inside OnValidate trips "SendMessage cannot be called during Awake/OnValidate".
            UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) { Rebuild(); } };
        }
#endif

        /// <summary>Regenerates the track mesh from the current spline.</summary>
        public void Rebuild()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "SplineTrack", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            }
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _mesh.Clear();

            SplineContainer container = GetComponent<SplineContainer>();
            Spline spline = container != null ? container.Spline : null;
            if (spline == null || spline.Count < 2) { return; }

            // Local space, so moving the object moves the track. Same t as the mover's container.Evaluate.
            // ponytail: under non-uniform scale local and world length ratios differ and carts drift off the rails.
            float length = spline.GetLength();
            if (length < 1e-4f) { return; }

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            int segments = Mathf.Max(2, Mathf.CeilToInt(length / _step));
            int rings = spline.Closed ? segments : segments + 1;
            var frames = new Frame[rings];
            Vector3 prevRight = Vector3.right;
            for (int i = 0; i < rings; i++)
            {
                frames[i] = FrameAt(spline, length * i / segments, _heightOffset, ref prevRight);
            }

            AddRail(frames, -1f, spline.Closed, verts, normals, uvs, tris);
            AddRail(frames, 1f, spline.Closed, verts, normals, uvs, tris);

            if (_tieSpacing > 0f)
            {
                prevRight = Vector3.right;
                int ties = spline.Closed ? Mathf.FloorToInt(length / _tieSpacing - 1e-4f) + 1 : Mathf.FloorToInt(length / _tieSpacing + 1e-4f) + 1;
                for (int k = 0; k < ties; k++)
                {
                    Frame f = FrameAt(spline, Mathf.Min(k * _tieSpacing, length), _heightOffset, ref prevRight);
                    Vector3 center = f.Position - f.Up * (_railRadius + _tieSize.y * 0.5f);
                    AddBox(center, f.Right * (_tieSize.x * 0.5f), f.Up * (_tieSize.y * 0.5f), f.Forward * (_tieSize.z * 0.5f), verts, normals, uvs, tris);
                }
            }

            _mesh.SetVertices(verts);
            _mesh.SetNormals(normals);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
        }

        private struct Frame
        {
            public Vector3 Position;
            public float Distance;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
        }

        // ponytail: carry-over frame, not true parallel transport; add rotation-minimising frames if loops twist.
        private static Frame FrameAt(Spline spline, float distance, float heightOffset, ref Vector3 prevRight)
        {
            float t = spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
            spline.Evaluate(t, out var position, out var tangent, out var up);
            Vector3 forward = ((Vector3)tangent).normalized;
            Vector3 right = Vector3.Cross((Vector3)up, forward);
            right = right.sqrMagnitude > 1e-6f ? right.normalized : prevRight;
            prevRight = right;
            Vector3 frameUp = Vector3.Cross(forward, right);
            return new Frame { Position = (Vector3)position + frameUp * heightOffset, Distance = distance, Forward = forward, Right = right, Up = frameUp };
        }

        private void AddRail(Frame[] frames, float side, bool closed, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            int start = verts.Count;
            int sides = _railSides;
            for (int i = 0; i < frames.Length; i++)
            {
                Frame f = frames[i];
                Vector3 center = f.Position + f.Right * (side * _gauge * 0.5f);
                for (int j = 0; j < sides; j++)
                {
                    float a = 2f * Mathf.PI * j / sides;
                    Vector3 n = f.Right * Mathf.Cos(a) + f.Up * Mathf.Sin(a);
                    verts.Add(center + n * _railRadius);
                    normals.Add(n);
                    uvs.Add(new Vector2((float)j / sides, f.Distance));
                }
            }

            int ringCount = closed ? frames.Length : frames.Length - 1;
            for (int i = 0; i < ringCount; i++)
            {
                int ring = start + i * sides;
                int next = start + (i + 1) % frames.Length * sides;
                for (int j = 0; j < sides; j++)
                {
                    int j1 = (j + 1) % sides;
                    int a = ring + j, b = ring + j1, c = next + j, d = next + j1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }
        }

        private static void AddBox(Vector3 center, Vector3 x, Vector3 y, Vector3 z, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            Vector3[] axes = { x, y, z };
            for (int face = 0; face < 6; face++)
            {
                Vector3 n = axes[face % 3] * (face < 3 ? 1f : -1f);
                Vector3 u = axes[(face + 1) % 3];
                Vector3 v = axes[(face + 2) % 3];
                Vector3 c = center + n;
                int s = verts.Count;
                verts.Add(c - u - v); verts.Add(c - u + v); verts.Add(c + u + v); verts.Add(c + u - v);
                Vector3 nn = n.normalized;
                for (int k = 0; k < 4; k++) { normals.Add(nn); }
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                // Unity front faces wind so Cross(p1-p0, p2-p0) points out; flip when this axis order doesn't.
                bool flip = Vector3.Dot(Vector3.Cross(verts[s + 1] - verts[s], verts[s + 2] - verts[s]), n) < 0f;
                if (flip) { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
                else { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
            }
        }
    }
}
