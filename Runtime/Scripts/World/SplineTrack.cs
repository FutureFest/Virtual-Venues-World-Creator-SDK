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
        [Tooltip("Thickness of each rail tube (metres).")]
        [Min(0.005f)] [SerializeField] private float _railRadius = 0.06f;
        [Tooltip("Corners around each rail tube. More = rounder rails, more triangles.")]
        [Range(3, 16)] [SerializeField] private int _railSides = 6;
        [Tooltip("Rail sample spacing along the track (metres). Smaller = smoother curves, more triangles.")]
        [Min(0.05f)] [SerializeField] private float _step = 0.5f;
        [Tooltip("Metres between ties. 0 = no ties.")]
        [Min(0f)] [SerializeField] private float _tieSpacing = 1f;
        [Tooltip("Size of each tie (metres): X = across the track, Y = thickness, Z = along the track.")]
        [SerializeField] private Vector3 _tieSize = new Vector3(1.5f, 0.08f, 0.2f);
        [Tooltip("Moves the rails along the track's up (metres). Negative = below the spline, to meet carts whose wheels sit below their pivot.")]
        [SerializeField] private float _heightOffset = 0f;

        [Header("Parts")]
        [Tooltip("Draw the left rail.")]
        [SerializeField] private bool _showLeftRail = true;
        [Tooltip("Material for the left rail. Empty = the Mesh Renderer's material.")]
        [SerializeField] private Material _leftRailMaterial = null;
        [Tooltip("Draw the right rail.")]
        [SerializeField] private bool _showRightRail = true;
        [Tooltip("Material for the right rail. Empty = the Mesh Renderer's material.")]
        [SerializeField] private Material _rightRailMaterial = null;
        [Tooltip("Draw the ties (the cross pieces under the rails).")]
        [SerializeField] private bool _showTies = true;
        [Tooltip("Material for the ties. Empty = the Mesh Renderer's material.")]
        [SerializeField] private Material _tieMaterial = null;
        [Tooltip("Draw support posts under the track. Each post drops until it hits a solid collider (ground, a tunnel " +
                 "roof, a building) or a Support Blocker, and never goes lower than Ground Height. Posts never poke through a tunnel.")]
        [SerializeField] private bool _showSupports = false;
        [Tooltip("Material for the supports. Empty = the Mesh Renderer's material.")]
        [SerializeField] private Material _supportMaterial = null;
        [Tooltip("Metres along the track between support posts.")]
        [Min(0.5f)] [SerializeField] private float _supportSpacing = 6f;
        [Tooltip("Thickness of each support post (metres, radius).")]
        [Min(0.01f)] [SerializeField] private float _supportRadius = 0.15f;
        [Tooltip("No post where the track is closer to the ground than this (metres).")]
        [Min(0f)] [SerializeField] private float _supportMinHeight = 1.5f;
        [Tooltip("Lowest a post can go, in this object's local space (0 = this object's own height). Posts that hit " +
                 "no collider on the way down stand here.")]
        [SerializeField] private float _groundHeight = 0f;
        [Tooltip("Where the track leans over this far or more (degrees from upright), it gets an arch standing over it " +
                 "instead of a post underneath: two legs, a crossbeam above the track and a hanger down to it. " +
                 "90 = on its side, 180 = upside down.")]
        [Range(10f, 180f)] [SerializeField] private float _archTilt = 90f;
        [Tooltip("Distance between an arch's two legs (metres). Widen it if the legs run through a nearby stretch of track, e.g. the way into a loop.")]
        [Min(0.5f)] [SerializeField] private float _archWidth = 5f;

        private static readonly List<SplineTrack> _active = new List<SplineTrack>();

        private Mesh _mesh = null;

        public float Gauge => _gauge;
        public float RailRadius => _railRadius;
        public int RailSides => _railSides;
        public float TieSpacing => _tieSpacing;
        public float HeightOffset => _heightOffset;
        public Mesh TrackMesh => _mesh;
        public float SupportSpacing => _supportSpacing;
        public float GroundHeight => _groundHeight;
        public bool ShowSupports => _showSupports;

        /// <summary>Set which parts draw and their materials (null = the renderer's material), then rebuild.</summary>
        public void ConfigureParts(bool leftRail, Material leftRailMaterial, bool rightRail, Material rightRailMaterial, bool ties, Material tieMaterial)
        {
            _showLeftRail = leftRail;
            _leftRailMaterial = leftRailMaterial;
            _showRightRail = rightRail;
            _rightRailMaterial = rightRailMaterial;
            _showTies = ties;
            _tieMaterial = tieMaterial;
            Rebuild();
        }

        /// <summary>Set the support posts (null material = the renderer's material), then rebuild.</summary>
        public void ConfigureSupports(bool show, Material material, float spacing, float radius, float minHeight, float groundHeight)
        {
            _showSupports = show;
            _supportMaterial = material;
            _supportSpacing = Mathf.Max(0.5f, spacing);
            _supportRadius = Mathf.Max(0.01f, radius);
            _supportMinHeight = Mathf.Max(0f, minHeight);
            _groundHeight = groundHeight;
            Rebuild();
        }

        /// <summary>Rebuilds every enabled track, e.g. after moving colliders that support posts stand on.</summary>
        public static void RebuildAll()
        {
            foreach (SplineTrack track in _active.ToArray()) { if (track != null) { track.Rebuild(); } }
        }

        private void OnEnable()
        {
            _active.Add(this);
            Rebuild();
#if UNITY_EDITOR
            Spline.Changed += OnSplineChanged;
#endif
        }

        // Other colliders in a loading world may register after our OnEnable; cast the posts again once they exist.
        private void Start()
        {
            if (Application.isPlaying && _showSupports) { Rebuild(); }
        }

        private void OnDisable()
        {
            _active.Remove(this);
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

        // Moving, adding or removing any collider in the editor re-casts the posts (so they follow a dragged tunnel).
        private static bool _rebuildQueued = false;

        [UnityEditor.InitializeOnLoadMethod]
        private static void HookColliderEdits()
        {
            UnityEditor.Undo.postprocessModifications += mods =>
            {
                foreach (UnityEditor.UndoPropertyModification mod in mods)
                {
                    Object target = mod.currentValue?.target;
                    if (target is Transform || target is Collider || target is TrackSupportBlocker) { QueueRebuildAll(); break; }
                }
                return mods;
            };
            UnityEditor.EditorApplication.hierarchyChanged += QueueRebuildAll;
        }

        /// <summary>Rebuilds every track once, on the next editor tick (coalesces a burst of edits).</summary>
        public static void QueueRebuildAll()
        {
            if (_rebuildQueued || _active.Count == 0) { return; }
            _rebuildQueued = true;
            UnityEditor.EditorApplication.delayCall += () => { _rebuildQueued = false; RebuildAll(); };
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
            var leftTris = new List<int>();
            var rightTris = new List<int>();
            var tieTris = new List<int>();
            var supportTris = new List<int>();

            int segments = Mathf.Max(2, Mathf.CeilToInt(length / _step));
            int rings = spline.Closed ? segments : segments + 1;
            var frames = new Frame[rings];
            Vector3 prevRight = Vector3.right;
            for (int i = 0; i < rings; i++)
            {
                frames[i] = FrameAt(spline, length * i / segments, _heightOffset, ref prevRight);
            }

            if (_showLeftRail) { AddRail(frames, -1f, spline.Closed, verts, normals, uvs, leftTris); }
            if (_showRightRail) { AddRail(frames, 1f, spline.Closed, verts, normals, uvs, rightTris); }

            if (_showTies && _tieSpacing > 0f)
            {
                prevRight = Vector3.right;
                int ties = spline.Closed ? Mathf.FloorToInt(length / _tieSpacing - 1e-4f) + 1 : Mathf.FloorToInt(length / _tieSpacing + 1e-4f) + 1;
                for (int k = 0; k < ties; k++)
                {
                    Frame f = FrameAt(spline, Mathf.Min(k * _tieSpacing, length), _heightOffset, ref prevRight);
                    Vector3 center = f.Position - f.Up * (_railRadius + _tieSize.y * 0.5f);
                    AddBox(center, f.Right * (_tieSize.x * 0.5f), f.Up * (_tieSize.y * 0.5f), f.Forward * (_tieSize.z * 0.5f), verts, normals, uvs, tieTris);
                }
            }

            if (_showSupports)
            {
                // Casts are in world space; autoSyncTransforms is off, so a just-moved collider is hit at its old spot.
                Physics.SyncTransforms();
                prevRight = Vector3.right;
                for (float d = _supportSpacing * 0.5f; d < length; d += _supportSpacing)
                {
                    Frame f = FrameAt(spline, d, _heightOffset, ref prevRight);
                    AddSupport(f, verts, normals, uvs, supportTris);
                }
            }

            _mesh.SetVertices(verts);
            _mesh.SetNormals(normals);
            _mesh.SetUVs(0, uvs);
            ApplyParts((leftTris, _leftRailMaterial), (rightTris, _rightRailMaterial), (tieTris, _tieMaterial), (supportTris, _supportMaterial));
            _mesh.RecalculateBounds();
        }

        // Submesh 0 = parts using the renderer's own material; one more submesh per override material.
        private void ApplyParts(params (List<int> tris, Material material)[] parts)
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            Material[] current = meshRenderer.sharedMaterials;
            var materials = new List<Material> { current.Length > 0 ? current[0] : null };
            var groups = new List<List<int>> { new List<int>() };
            foreach (var part in parts)
            {
                if (part.tris.Count == 0) { continue; }
                int group = part.material == null ? 0 : materials.IndexOf(part.material);
                if (group < 0)
                {
                    materials.Add(part.material);
                    groups.Add(new List<int>());
                    group = groups.Count - 1;
                }
                groups[group].AddRange(part.tris);
            }

            _mesh.subMeshCount = groups.Count;
            for (int i = 0; i < groups.Count; i++) { _mesh.SetTriangles(groups[i], i); }

            // Only write when it changed, so reopening a scene doesn't mark it dirty.
            bool same = current.Length == materials.Count;
            for (int i = 0; same && i < current.Length; i++) { same = current[i] == materials[i]; }
            if (!same) { meshRenderer.sharedMaterials = materials.ToArray(); }
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

        // Local height a post from `top` stands on: the first collider that stops supports, else Ground Height.
        private float PostBottom(Vector3 top)
        {
            if (top.y <= _groundHeight) { return _groundHeight; }
            Vector3 worldTop = transform.TransformPoint(top);
            Vector3 worldFloor = transform.TransformPoint(new Vector3(top.x, _groundHeight, top.z));
            Vector3 toFloor = worldFloor - worldTop;
            RaycastHit[] hits = Physics.RaycastAll(worldTop, toFloor.normalized, toFloor.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            float best = toFloor.magnitude;
            Vector3 bottom = worldFloor;
            foreach (RaycastHit hit in hits)
            {
                if (hit.distance < best && StopsSupports(hit.collider)) { best = hit.distance; bottom = hit.point; }
            }
            return transform.InverseTransformPoint(bottom).y;
        }

        /// <summary>
        /// True when support posts stand on this collider instead of passing through: any solid collider does;
        /// a trigger only with a <see cref="TrackSupportBlocker"/>; a blocker set to Let Supports Through never does.
        /// This track's own children and carts (anything under a <see cref="SplineMover"/>) are ignored.
        /// </summary>
        public bool StopsSupports(Collider other)
        {
            if (other.transform.IsChildOf(transform)) { return false; }
            if (other.GetComponentInParent<SplineMover>() != null) { return false; }
            TrackSupportBlocker blocker = other.GetComponent<TrackSupportBlocker>();
            if (blocker != null) { return blocker.Mode == TrackSupportBlocker.BlockMode.Block; }
            return !other.isTrigger;
        }

        // Upright track: a post from under the ties straight down. Tilted over: an arch standing over the track
        // (two legs, a crossbeam above it, a hanger down to the ties), so nothing runs through the rails.
        private void AddSupport(Frame f, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            const float clearance = 0.5f;
            Vector3 attach = f.Position - f.Up * (_railRadius + _tieSize.y);
            if (Vector3.Angle(f.Up, Vector3.up) < _archTilt)
            {
                float bottomY = PostBottom(attach);
                if (attach.y - bottomY < _supportMinHeight) { return; }
                AddTube(attach, new Vector3(attach.x, bottomY, attach.z), verts, normals, uvs, tris);
                return;
            }

            // ponytail: legs aren't checked against other stretches of track; add avoidance if it bothers someone.
            Vector3 flat = new Vector3(f.Forward.x, 0f, f.Forward.z);
            Vector3 across = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat) : new Vector3(f.Right.x, 0f, f.Right.z);
            across = across.sqrMagnitude > 1e-6f ? across.normalized : Vector3.right;
            float halfWidth = _tieSize.x * 0.5f;
            float beamY = f.Position.y + halfWidth + _railRadius + _tieSize.y + clearance;
            Vector3 centre = new Vector3(f.Position.x, beamY, f.Position.z);
            // Never narrower than the ties, or the legs would clip the track itself.
            float halfSpan = Mathf.Max(_archWidth * 0.5f, halfWidth + _supportRadius * 2f);
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 legTop = centre + across * (side * halfSpan);
                AddTube(legTop, new Vector3(legTop.x, PostBottom(legTop), legTop.z), verts, normals, uvs, tris);
            }
            AddTube(centre + across * halfSpan, centre - across * halfSpan, verts, normals, uvs, tris);
            Vector3 hangerTop = centre + across * Vector3.Dot(attach - centre, across);
            hangerTop.y = beamY;
            AddTube(attach, hangerTop, verts, normals, uvs, tris);
        }

        // An 8-sided tube of the support radius from `from` to `to` (local space). No end caps.
        private void AddTube(Vector3 from, Vector3 to, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            const int sides = 8;
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-4f) { return; }
            axis /= length;
            // Cross(u, v) = axis keeps the triangles facing outward.
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            int start = verts.Count;
            for (int j = 0; j < sides; j++)
            {
                float a = 2f * Mathf.PI * j / sides;
                Vector3 n = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                verts.Add(from + n * _supportRadius);
                verts.Add(to + n * _supportRadius);
                normals.Add(n);
                normals.Add(n);
                uvs.Add(new Vector2((float)j / sides, length));
                uvs.Add(new Vector2((float)j / sides, 0f));
            }
            for (int j = 0; j < sides; j++)
            {
                int t0 = start + j * 2, b0 = t0 + 1;
                int t1 = start + (j + 1) % sides * 2, b1 = t1 + 1;
                tris.Add(t0); tris.Add(t1); tris.Add(b0);
                tris.Add(t1); tris.Add(b1); tris.Add(b0);
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
