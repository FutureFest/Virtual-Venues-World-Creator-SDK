using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace VirtualVenues.WorldCreator
{
    /// <summary>One prop a <see cref="Pathway"/> places, in the path object's local space.</summary>
    public struct PathwayPlacement
    {
        public int Rule;
        /// <summary>Which spline of the container it belongs to (-1 = a junction prop).</summary>
        public int Spline;
        public GameObject Prefab;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>Final local scale (the prefab's own scale times the rule's random size).</summary>
        public Vector3 Scale;
    }

    /// <summary>
    /// Turns the splines on this object into finished paths: a ground strip plus props (lamp posts, fences, plants)
    /// placed by <see cref="PathwayRule"/>s. Every spline in the container is a path; linked path points (Draw Splines
    /// from an existing point links them) are junctions, where the strips meet in a patch and props stay off the
    /// joining paths. Only the splines, rules and seed are saved; the strip and props are rebuilt on load (in the
    /// editor and in the app), so editing the splines moves everything. Bake (Inspector) turns the props into normal
    /// saved objects that no longer rebuild.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("VirtualVenues/Pathway")]
    [RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
    public class Pathway : MonoBehaviour
    {
        public const string GeneratedRootName = "Pathway Props";
        public const string BakedRootName = "Pathway Props (Baked)";

        // Ground search: start this far above the path, look this far down.
        private const float RayUp = 5f;
        private const float RayDown = 50f;
        // Props stay this far (metres) outside the edge of every other path in the network.
        private const float KeepOutMargin = 0.25f;

        [Tooltip("Change to reshuffle every random choice (which prop, nudge, turn, size). The same number always gives the same path.")]
        [SerializeField] private int _seed = 1;
        [Tooltip("Drop the ground strip, and props with Follow Ground, onto the colliders under the path (terrain, floors). " +
                 "Off = everything sits on the spline itself.")]
        [SerializeField] private bool _snapToGround = true;

        [Header("Ground")]
        [Tooltip("Lay a flat strip along the path, using Ground Material (or the Mesh Renderer's material).")]
        [SerializeField] private bool _showGround = true;
        [Tooltip("Path width in metres. Props on the side lanes stand just outside it.")]
        [Min(0.1f)] [SerializeField] private float _width = 3f;
        [Tooltip("Metres of path one copy of the ground texture covers.")]
        [Min(0.1f)] [SerializeField] private float _textureLength = 3f;
        [Tooltip("Lift the strip this far above the ground (metres) so it doesn't flicker against it.")]
        [SerializeField] private float _groundHeight = 0.05f;
        [Tooltip("Material for the strip, e.g. a URP/Lit gravel or paving material. Empty = the Mesh Renderer's material.")]
        [SerializeField] private Material _groundMaterial = null;
        [Tooltip("Strip sample spacing along the path (metres). Smaller = smoother curves, more triangles.")]
        [Min(0.1f)] [SerializeField] private float _step = 0.5f;
        [Tooltip("Where paths meet (linked path points), the strips stop this far from the middle and a patch fills the " +
                 "gap. 0 = automatic (half the width, more where paths meet at a sharp angle).")]
        [Min(0f)] [SerializeField] private float _junctionRadius = 0f;

        [Header("Props")]
        [Tooltip("Each rule is one layer of props, e.g. Lamps, Fence, Flowers. Earlier rules win keep-clear clashes.")]
        [SerializeField] private List<PathwayRule> _rules = new List<PathwayRule>();

        [HideInInspector] [SerializeField] private bool _baked = false;

        private Mesh _mesh = null;
        private Transform _snapIgnoreRoot = null;

        public int Seed => _seed;
        public float Width => _width;
        public bool ShowGround => _showGround;
        public bool SnapToGround => _snapToGround;
        public bool IsBaked => _baked;
        public List<PathwayRule> Rules => _rules;
        public Mesh GroundMesh => _mesh;
        public float TextureLength => _textureLength;
        public float GroundHeight => _groundHeight;
        public Material GroundMaterial => _groundMaterial;

        /// <summary>
        /// Colliders under this object are never ground, e.g. the placed object a loader hangs the path on. Not saved;
        /// set it before <see cref="Configure"/>.
        /// </summary>
        public Transform SnapIgnoreRoot { get => _snapIgnoreRoot; set => _snapIgnoreRoot = value; }

        /// <summary>Set the main options and the rules (copied), then rebuild.</summary>
        public void Configure(int seed, bool snapToGround, bool showGround, float width, Material groundMaterial, IEnumerable<PathwayRule> rules)
        {
            _seed = seed;
            _snapToGround = snapToGround;
            _showGround = showGround;
            _width = Mathf.Max(0.1f, width);
            _groundMaterial = groundMaterial;
            _rules = new List<PathwayRule>();
            foreach (PathwayRule rule in rules) { if (rule != null) { _rules.Add(rule.Clone()); } }
            Rebuild();
        }

        /// <summary>Copy a kit's ground and rules onto this path (keeps the seed and splines), then rebuild.</summary>
        public void ApplyPreset(PathwayPreset preset)
        {
            if (preset == null) { return; }
            _showGround = preset.ShowGround;
            _width = preset.Width;
            _textureLength = preset.TextureLength;
            _groundHeight = preset.GroundHeight;
            _groundMaterial = preset.GroundMaterial;
            _rules = new List<PathwayRule>();
            foreach (PathwayRule rule in preset.Rules) { if (rule != null) { _rules.Add(rule.Clone()); } }
            Rebuild();
        }

        /// <summary>Baked: props are normal saved children and are not rebuilt. Use the Inspector's Bake / Unbake.</summary>
        public void SetBaked(bool baked)
        {
            _baked = baked;
            Rebuild();
        }

        private void OnEnable()
        {
            Rebuild();
#if UNITY_EDITOR
            Spline.Changed += OnSplineChanged;
            UnityEditor.Undo.postprocessModifications += OnModifications;
#endif
        }

        // Ground colliders in a loading world may register after our OnEnable; snap again once they exist.
        private void Start()
        {
            if (Application.isPlaying && _snapToGround) { Rebuild(); }
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            Spline.Changed -= OnSplineChanged;
            UnityEditor.Undo.postprocessModifications -= OnModifications;
#endif
        }

        private void OnDestroy()
        {
            if (_mesh == null) { return; }
            if (Application.isPlaying) { Destroy(_mesh); } else { DestroyImmediate(_mesh); }
        }

#if UNITY_EDITOR
        private bool _rebuildQueued = false;

        private void OnSplineChanged(Spline spline, int knot, SplineModification modification)
        {
            SplineContainer container = GetComponent<SplineContainer>();
            if (container == null) { return; }
            foreach (Spline own in container.Splines) { if (own == spline) { QueueRebuild(); return; } }
        }

        // Moving the path object moves where it meets the ground; adding, removing and linking splines is recorded on
        // the container.
        // ponytail: moving other colliders (terrain, floors) doesn't re-snap; press Rebuild. Hook them like SplineTrack if it bites.
        private UnityEditor.UndoPropertyModification[] OnModifications(UnityEditor.UndoPropertyModification[] mods)
        {
            foreach (UnityEditor.UndoPropertyModification mod in mods)
            {
                Object target = mod.currentValue?.target;
                if (target == transform || (target is SplineContainer && target == GetComponent<SplineContainer>())) { QueueRebuild(); break; }
            }
            return mods;
        }

        private void OnValidate()
        {
            QueueRebuild();
        }

        // Coalesces a drag's burst of edits into one rebuild. Rebuilding inside OnValidate trips
        // "SendMessage cannot be called during Awake/OnValidate".
        private void QueueRebuild()
        {
            if (_rebuildQueued) { return; }
            _rebuildQueued = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                _rebuildQueued = false;
                if (this != null && isActiveAndEnabled) { Rebuild(); }
            };
        }
#endif

        /// <summary>Regenerates the ground strip and (unless baked) the props from the current splines and rules.</summary>
        public void Rebuild()
        {
            if (_snapToGround) { Physics.SyncTransforms(); }
            Network net = BuildNetwork();
            BuildGround(net);
            ClearGenerated();
            if (_baked || !gameObject.scene.IsValid()) { return; }

            List<PathwayPlacement> placements = ComputePlacements(net);
            if (placements.Count == 0) { return; }
            var root = new GameObject(GeneratedRootName);
            if (!Application.isPlaying) { root.hideFlags = HideFlags.DontSave | HideFlags.NotEditable; }
            root.transform.SetParent(transform, false);
            foreach (PathwayPlacement p in placements)
            {
                GameObject instance = Instantiate(p.Prefab, root.transform);
                instance.transform.SetLocalPositionAndRotation(p.Position, p.Rotation);
                instance.transform.localScale = p.Scale;
                // A prefab handed over as an inactive or hidden prototype (e.g. by a content loader) still shows up.
                if (!instance.activeSelf) { instance.SetActive(true); }
                instance.hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSave;
            }
        }

        /// <summary>Removes the generated (not baked) props.</summary>
        public void ClearGenerated()
        {
            Transform root = transform.Find(GeneratedRootName);
            while (root != null)
            {
                // Rename first: Destroy is deferred in play mode and Find would return it again.
                root.name = GeneratedRootName + " (old)";
                if (Application.isPlaying) { Destroy(root.gameObject); } else { DestroyImmediate(root.gameObject); }
                root = transform.Find(GeneratedRootName);
            }
        }

        /// <summary>Every prop the rules place, without creating anything. Same seed and splines = same list.</summary>
        public List<PathwayPlacement> ComputePlacements()
        {
            return ComputePlacements(BuildNetwork());
        }

        /// <summary>Where paths meet, in this object's local space: one entry per group of linked path points.</summary>
        public List<Vector3> JunctionPositions()
        {
            var result = new List<Vector3>();
            foreach (Junction j in BuildNetwork().Junctions) { result.Add(j.Centre); }
            return result;
        }

        private List<PathwayPlacement> ComputePlacements(Network net)
        {
            var result = new List<PathwayPlacement>();
            var taken = new List<(Vector3 position, float radius)>();
            for (int r = 0; r < _rules.Count; r++)
            {
                PathwayRule rule = _rules[r];
                if (rule == null || !rule.Enabled) { continue; }
                if (rule.Slot == PathwaySlot.Junctions)
                {
                    PlaceAtJunctions(net, r, rule, taken, result);
                    continue;
                }
                for (int s = 0; s < net.Splines.Count; s++)
                {
                    if (net.Splines[s] != null) { PlaceAlong(net, s, r, rule, taken, result); }
                }
            }
            return result;
        }

        private void PlaceAlong(Network net, int s, int r, PathwayRule rule, List<(Vector3 position, float radius)> taken, List<PathwayPlacement> result)
        {
            Spline spline = net.Splines[s];
            float length = net.Lengths[s];
            var rng = new System.Random(RuleSeed(_seed, r, s));
            float start = Mathf.Min(rule.StartInset, length);
            float end = spline.Closed ? length : Mathf.Max(start, length - rule.EndInset);
            bool alternate = false;

            foreach (float slotDistance in SlotDistances(net, s, rule, start, end))
            {
                // Draw every random number up front, in a fixed order, so skipping one prop never shifts the next.
                double jitter = rng.NextDouble();
                float distance = slotDistance;
                if (rule.Slot == PathwaySlot.Every && rule.Jitter > 0f)
                {
                    distance = Mathf.Clamp(distance + ((float)jitter - 0.5f) * rule.Jitter * rule.Spacing, start, end);
                }
                foreach (float side in Sides(rule.Lane, ref alternate))
                {
                    double chance = rng.NextDouble(), pick = rng.NextDouble(), turn = rng.NextDouble(), scale = rng.NextDouble();
                    if (chance >= rule.Chance) { continue; }
                    GameObject prefab = rule.Pick(pick);
                    if (prefab == null) { continue; }

                    float lateral = side == 0f ? rule.Offset : side * (_width * 0.5f + rule.Offset);
                    float size = Mathf.Lerp(rule.MinScale, Mathf.Max(rule.MinScale, rule.MaxScale), (float)scale);
                    if (rule.Slot == PathwaySlot.Segments)
                    {
                        // Only the stretches clear of other paths, so a fence stops at a joining path's edge.
                        foreach (var (from, to) in FreeStretches(net, s, distance, Mathf.Min(distance + rule.Spacing, end), lateral))
                        {
                            if (to - from < Mathf.Min(0.3f, rule.Spacing * 0.5f)) { continue; }
                            Vector3 a = SidePoint(spline, from, lateral, rule.FollowGround, out _);
                            Vector3 b = SidePoint(spline, to, lateral, rule.FollowGround, out Vector3 segmentRight);
                            Vector3 sizeScale = Vector3.one * size;
                            // Stretch each piece to reach the next, so a fence stays closed around curves.
                            sizeScale.z *= (b - a).magnitude / rule.Spacing;
                            Add(result, taken, rule, r, s, prefab, (a + b) * 0.5f, b - a, segmentRight, side, (float)turn, sizeScale);
                        }
                        continue;
                    }

                    Vector3 position = SidePoint(spline, distance, lateral, rule.FollowGround, out Vector3 right);
                    if (KeptOut(net, s, position, lateral)) { continue; }
                    Add(result, taken, rule, r, s, prefab, position, Vector3.Cross(right, Vector3.up), right, side, (float)turn, Vector3.one * size);
                }
            }
        }

        // Junction props: one per corner between neighbouring arms, or one in the middle on the Center lane.
        private void PlaceAtJunctions(Network net, int r, PathwayRule rule, List<(Vector3 position, float radius)> taken, List<PathwayPlacement> result)
        {
            var rng = new System.Random(RuleSeed(_seed, r, 0));
            float reach = _width * 0.5f + rule.Offset;
            foreach (Junction j in net.Junctions)
            {
                int count = rule.Lane == PathwayLane.Center ? 1 : j.Arms.Count;
                for (int i = 0; i < count; i++)
                {
                    double chance = rng.NextDouble(), pick = rng.NextDouble(), turn = rng.NextDouble(), scale = rng.NextDouble();
                    if (chance >= rule.Chance) { continue; }
                    GameObject prefab = rule.Pick(pick);
                    if (prefab == null) { continue; }
                    Vector3 size = Vector3.one * Mathf.Lerp(rule.MinScale, Mathf.Max(rule.MinScale, rule.MaxScale), (float)scale);

                    Vector3 position;
                    Vector3 along;
                    Vector3 right;
                    float side;
                    if (rule.Lane == PathwayLane.Center)
                    {
                        along = Flat3(j.Arms[0].Out);
                        right = Vector3.Cross(Vector3.up, along);
                        position = j.Centre + right * rule.Offset;
                        side = 0f;
                    }
                    else
                    {
                        // Arms are sorted by angle; the corner sits on the bisector, `reach` from both arms' middles.
                        float a0 = j.Arms[i].Angle;
                        float a1 = i + 1 < j.Arms.Count ? j.Arms[i + 1].Angle : j.Arms[0].Angle + 360f;
                        float gap = a1 - a0;
                        float mid = (a0 + gap * 0.5f) * Mathf.Deg2Rad;
                        along = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                        float sin = Mathf.Sin(gap * 0.5f * Mathf.Deg2Rad);
                        position = j.Centre + along * (sin > 1e-3f ? Mathf.Min(reach / sin, reach * 3f) : reach * 3f);
                        // Facing() reads "toward the path" as -right * side, so point right out from the middle.
                        right = along;
                        side = 1f;
                    }
                    if (_snapToGround && rule.FollowGround) { position = Snap(position); }
                    Add(result, taken, rule, r, -1, prefab, position, along, right, side, (float)turn, size);
                }
            }
        }

        private static Vector3 Flat3(Vector2 v) => new Vector3(v.x, 0f, v.y);

        private void Add(List<PathwayPlacement> result, List<(Vector3 position, float radius)> taken, PathwayRule rule, int r, int s,
            GameObject prefab, Vector3 position, Vector3 along, Vector3 right, float side, float turn, Vector3 sizeScale)
        {
            if (rule.Clearance > 0f)
            {
                foreach (var t in taken)
                {
                    Vector2 d = new Vector2(t.position.x - position.x, t.position.z - position.z);
                    if (d.magnitude < t.radius + rule.Clearance) { return; }
                }
                taken.Add((position, rule.Clearance));
            }
            result.Add(new PathwayPlacement
            {
                Rule = r,
                Spline = s,
                Prefab = prefab,
                Position = position,
                Rotation = Facing(rule, along, right, side, turn),
                Scale = Vector3.Scale(prefab.transform.localScale, sizeScale),
            });
        }

        private static int RuleSeed(int seed, int rule, int spline)
        {
            unchecked { return seed * 486187739 + (rule + 1) * 16777619 + spline * 7919; }
        }

        private static IEnumerable<float> SlotDistances(Network net, int s, PathwayRule rule, float start, float end)
        {
            Spline spline = net.Splines[s];
            switch (rule.Slot)
            {
                // A path end joined to another path is a junction, not an end.
                case PathwaySlot.Start:
                    if (spline.Closed || !net.Linked(s, 0)) { yield return start; }
                    break;
                case PathwaySlot.End:
                    if (spline.Closed || !net.Linked(s, spline.Count - 1)) { yield return end; }
                    break;
                case PathwaySlot.Corners:
                    for (int k = 0; k < spline.Count; k++)
                    {
                        if (!spline.Closed && (k == 0 || k == spline.Count - 1)) { continue; }
                        if (net.Linked(s, k)) { continue; }
                        float d = spline.ConvertIndexUnit(k, PathIndexUnit.Knot, PathIndexUnit.Distance);
                        if (d < start || d > end) { continue; }
                        if (TurnAngle(spline, k) >= rule.CornerAngle) { yield return d; }
                    }
                    break;
                case PathwaySlot.Segments:
                    for (float d = start; d < end - 1e-3f; d += rule.Spacing) { yield return d; }
                    break;
                default:
                    // Closed: stop short of the end, which is the start again.
                    for (float d = start; spline.Closed ? d < end - 1e-3f : d <= end + 1e-3f; d += rule.Spacing) { yield return Mathf.Min(d, end); }
                    break;
            }
        }

        // How sharply the path turns at knot k, seen from above (degrees), from the straight lines to its neighbours.
        private static float TurnAngle(Spline spline, int k)
        {
            int count = spline.Count;
            Vector3 prev = spline[(k - 1 + count) % count].Position;
            Vector3 here = spline[k].Position;
            Vector3 next = spline[(k + 1) % count].Position;
            Vector3 a = here - prev, b = next - here;
            a.y = 0f;
            b.y = 0f;
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) { return 0f; }
            return Vector3.Angle(a, b);
        }

        private static IEnumerable<float> Sides(PathwayLane lane, ref bool alternate)
        {
            switch (lane)
            {
                case PathwayLane.Left: return new[] { -1f };
                case PathwayLane.Right: return new[] { 1f };
                case PathwayLane.BothSides: return new[] { -1f, 1f };
                case PathwayLane.Alternating:
                    alternate = !alternate;
                    return new[] { alternate ? -1f : 1f };
                default: return new[] { 0f };
            }
        }

        private static Quaternion Facing(PathwayRule rule, Vector3 along, Vector3 right, float side, float turnRoll)
        {
            Vector3 flat = new Vector3(along.x, 0f, along.z);
            Vector3 forward;
            switch (rule.Facing)
            {
                // A prop on the left (side -1) faces the middle of the path by looking right.
                case PathwayFacing.TowardPath: forward = side == 0f ? flat : -right * side; break;
                case PathwayFacing.AwayFromPath: forward = side == 0f ? -flat : right * side; break;
                case PathwayFacing.Random: forward = Quaternion.Euler(0f, turnRoll * 360f, 0f) * Vector3.forward; break;
                default: forward = rule.Slot == PathwaySlot.Segments ? along : flat; break;
            }
            if (forward.sqrMagnitude < 1e-8f) { forward = Vector3.forward; }
            float yaw = rule.Facing == PathwayFacing.Random ? 0f : (turnRoll * 2f - 1f) * rule.RandomTurn;
            return Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
        }

        // Local point `lateral` metres to the right of the path at `distance`, dropped to the ground when asked.
        // The path's sideways direction stays level, so paths never bank like a coaster track.
        private Vector3 SidePoint(Spline spline, float distance, float lateral, bool followGround, out Vector3 right)
        {
            float t = spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
            spline.Evaluate(t, out var position, out var tangent, out _);
            Vector3 forward = new Vector3(tangent.x, 0f, tangent.z);
            if (forward.sqrMagnitude < 1e-8f)
            {
                // Exactly on a linear knot the tangent is zero; use the direction between points either side.
                Vector3 ahead = spline.EvaluatePosition(Mathf.Min(1f, t + 1e-3f));
                Vector3 behind = spline.EvaluatePosition(Mathf.Max(0f, t - 1e-3f));
                forward = new Vector3(ahead.x - behind.x, 0f, ahead.z - behind.z);
            }
            if (forward.sqrMagnitude < 1e-10f) { forward = Vector3.forward; }
            right = Vector3.Cross(Vector3.up, forward.normalized);
            Vector3 point = (Vector3)position + right * lateral;
            return _snapToGround && followGround ? Snap(point) : point;
        }

        // Drops a local point onto the closest solid collider below it that isn't part of this path or its parents.
        private Vector3 Snap(Vector3 local)
        {
            Vector3 top = transform.TransformPoint(local + Vector3.up * RayUp);
            RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, RayUp + RayDown, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = local;
            foreach (RaycastHit hit in hits)
            {
                // Never this path's own props, nor the object it sits on (a path added to a placed prop).
                Transform other = hit.collider.transform;
                if (hit.distance >= best || other.IsChildOf(transform) || transform.IsChildOf(other)) { continue; }
                if (_snapIgnoreRoot != null && other.IsChildOf(_snapIgnoreRoot)) { continue; }
                best = hit.distance;
                point = transform.InverseTransformPoint(hit.point);
            }
            return point;
        }

        // ---- Network: the splines, their outlines and the junctions, worked out once per rebuild. ----

        private struct Arm
        {
            public int Spline;
            public float KnotDistance;
            /// <summary>-1 = the arm runs back toward the spline's start, +1 = on toward its end.</summary>
            public float Sign;
            /// <summary>Metres along the spline where the strip stops short of the junction (past 0 or the length on a loop).</summary>
            public float Trim;
            /// <summary>Level direction from the junction out along this arm, and its angle (degrees, from +X toward +Z).</summary>
            public Vector2 Out;
            public float Angle;
        }

        private sealed class Junction
        {
            public Vector3 Centre;
            public readonly List<Arm> Arms = new List<Arm>();
        }

        private sealed class Network
        {
            public SplineContainer Container;
            // Indexed like the container's splines; null where a spline is too short to be a path.
            public readonly List<Spline> Splines = new List<Spline>();
            public readonly List<float> Lengths = new List<float>();
            public readonly List<Vector2[]> Outlines = new List<Vector2[]>();
            public readonly List<Junction> Junctions = new List<Junction>();

            public bool Linked(int s, int k) => Container.KnotLinkCollection.GetKnotLinks(new SplineKnotIndex(s, k)).Count > 1;
        }

        private Network BuildNetwork()
        {
            var net = new Network { Container = GetComponent<SplineContainer>() };
            if (net.Container == null) { return net; }
            IReadOnlyList<Spline> splines = net.Container.Splines;
            for (int s = 0; s < splines.Count; s++)
            {
                Spline spline = splines[s];
                float length = spline != null && spline.Count >= 2 ? spline.GetLength() : 0f;
                bool usable = length > 1e-3f;
                net.Splines.Add(usable ? spline : null);
                net.Lengths.Add(length);
                if (!usable) { net.Outlines.Add(null); continue; }
                int n = Mathf.Max(2, Mathf.CeilToInt(length) + 1);
                var outline = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = (Vector3)spline.EvaluatePosition((float)i / (n - 1));
                    outline[i] = new Vector2(p.x, p.z);
                }
                net.Outlines.Add(outline);
            }

            var seen = new HashSet<SplineKnotIndex>();
            float half = _width * 0.5f;
            for (int s = 0; s < net.Splines.Count; s++)
            {
                Spline spline = net.Splines[s];
                if (spline == null) { continue; }
                for (int k = 0; k < spline.Count; k++)
                {
                    var index = new SplineKnotIndex(s, k);
                    if (seen.Contains(index)) { continue; }
                    IReadOnlyList<SplineKnotIndex> links = net.Container.KnotLinkCollection.GetKnotLinks(index);
                    if (links.Count < 2) { continue; }
                    var junction = new Junction { Centre = spline[k].Position };
                    foreach (SplineKnotIndex link in links)
                    {
                        seen.Add(link);
                        AddArms(net, link, junction);
                    }
                    if (junction.Arms.Count < 2) { continue; }
                    junction.Arms.Sort((a, b) => a.Angle.CompareTo(b.Angle));
                    TrimArms(net, junction, half);
                    net.Junctions.Add(junction);
                }
            }
            return net;
        }

        // One arm per direction a linked spline leaves the junction: back toward its start and on toward its end.
        private static void AddArms(Network net, SplineKnotIndex link, Junction junction)
        {
            if (link.Spline < 0 || link.Spline >= net.Splines.Count) { return; }
            Spline spline = net.Splines[link.Spline];
            if (spline == null || link.Knot < 0 || link.Knot >= spline.Count) { return; }
            float d = spline.ConvertIndexUnit(link.Knot, PathIndexUnit.Knot, PathIndexUnit.Distance);
            float length = net.Lengths[link.Spline];
            Vector3 here = spline.EvaluatePosition(spline.ConvertIndexUnit(d, PathIndexUnit.Distance, PathIndexUnit.Normalized));
            foreach (float sign in new[] { -1f, 1f })
            {
                bool hasArm = spline.Closed || (sign < 0f ? link.Knot > 0 : link.Knot < spline.Count - 1);
                if (!hasArm) { continue; }
                // Direction from a point a little way along the arm: linear knots have no tangent at the knot itself.
                float step = Mathf.Min(0.5f, length * 0.25f);
                float along = Wrap(d + sign * step, length, spline.Closed);
                Vector3 there = spline.EvaluatePosition(spline.ConvertIndexUnit(along, PathIndexUnit.Distance, PathIndexUnit.Normalized));
                var dir = new Vector2(there.x - here.x, there.z - here.z);
                if (dir.sqrMagnitude < 1e-8f) { continue; }
                dir.Normalize();
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                junction.Arms.Add(new Arm { Spline = link.Spline, KnotDistance = d, Sign = sign, Out = dir, Angle = angle < 0f ? angle + 360f : angle });
            }
        }

        // Pull each strip back far enough that neighbouring arms don't overlap outside the patch.
        private void TrimArms(Network net, Junction junction, float half)
        {
            float minGap = 360f;
            for (int i = 0; i < junction.Arms.Count; i++)
            {
                float a0 = junction.Arms[i].Angle;
                float a1 = i + 1 < junction.Arms.Count ? junction.Arms[i + 1].Angle : junction.Arms[0].Angle + 360f;
                minGap = Mathf.Min(minGap, a1 - a0);
            }
            float automatic = Mathf.Min(half / Mathf.Tan(Mathf.Max(minGap, 10f) * 0.5f * Mathf.Deg2Rad), half * 4f);
            float radius = Mathf.Max(_junctionRadius > 0f ? _junctionRadius : half, automatic);
            for (int i = 0; i < junction.Arms.Count; i++)
            {
                Arm arm = junction.Arms[i];
                arm.Trim = arm.KnotDistance + arm.Sign * radius;
                if (!net.Splines[arm.Spline].Closed) { arm.Trim = Mathf.Clamp(arm.Trim, 0f, net.Lengths[arm.Spline]); }
                junction.Arms[i] = arm;
            }
        }

        // True when a local point is on (or within the margin of) any path in the network other than spline `own`.
        // A side-lane prop also stays out of the other paths' matching lane, so two fences meet at a corner instead
        // of crossing (the small give keeps a fence running straight past a branch from being nicked).
        private bool KeptOut(Network net, int own, Vector3 point, float lateral)
        {
            float limit = Mathf.Max(_width * 0.5f + KeepOutMargin, Mathf.Abs(lateral) - 0.05f);
            float limitSq = limit * limit;
            var p = new Vector2(point.x, point.z);
            for (int t = 0; t < net.Outlines.Count; t++)
            {
                Vector2[] line = net.Outlines[t];
                if (t == own || line == null) { continue; }
                for (int i = 0; i < line.Length - 1; i++)
                {
                    Vector2 ab = line[i + 1] - line[i];
                    float u = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - line[i], ab) / ab.sqrMagnitude) : 0f;
                    if ((line[i] + ab * u - p).sqrMagnitude < limitSq) { return true; }
                }
            }
            return false;
        }

        // The parts of [d0, d1] at `lateral` on spline s that stay clear of other paths (edges found to a few cm).
        private List<(float from, float to)> FreeStretches(Network net, int s, float d0, float d1, float lateral)
        {
            var result = new List<(float, float)>();
            Spline spline = net.Splines[s];
            bool Free(float d) => !KeptOut(net, s, SidePoint(spline, d, lateral, false, out _), lateral);
            int n = Mathf.Max(2, Mathf.CeilToInt((d1 - d0) / 0.5f) + 1);
            float runStart = d0;
            float prev = d0;
            bool prevFree = Free(d0);
            for (int i = 1; i < n; i++)
            {
                float d = Mathf.Lerp(d0, d1, (float)i / (n - 1));
                bool free = Free(d);
                if (free != prevFree)
                {
                    float a = prev, b = d;
                    for (int k = 0; k < 6; k++)
                    {
                        float m = (a + b) * 0.5f;
                        if (Free(m) == prevFree) { a = m; } else { b = m; }
                    }
                    // The edge on the free side.
                    if (prevFree) { result.Add((runStart, a)); } else { runStart = b; }
                }
                prev = d;
                prevFree = free;
            }
            if (prevFree) { result.Add((runStart, d1)); }
            return result;
        }

        // The stretches of spline s the strip covers: all of it except where it runs into a junction.
        private static List<(float from, float to)> StripRuns(Network net, int s)
        {
            float length = net.Lengths[s];
            bool closed = net.Splines[s].Closed;
            var gaps = new List<(float from, float to)>();
            foreach (Junction j in net.Junctions)
            {
                foreach (Arm arm in j.Arms)
                {
                    if (arm.Spline != s) { continue; }
                    float from = Mathf.Min(arm.KnotDistance, arm.Trim), to = Mathf.Max(arm.KnotDistance, arm.Trim);
                    if (closed && from < 0f) { gaps.Add((from + length, length)); from = 0f; }
                    if (closed && to > length) { gaps.Add((0f, to - length)); to = length; }
                    gaps.Add((from, to));
                }
            }
            gaps.Sort((a, b) => a.from.CompareTo(b.from));
            var runs = new List<(float, float)>();
            float cursor = 0f;
            foreach (var (from, to) in gaps)
            {
                if (from > cursor + 1e-3f) { runs.Add((cursor, from)); }
                cursor = Mathf.Max(cursor, to);
            }
            if (length > cursor + 1e-3f) { runs.Add((cursor, length)); }
            return runs;
        }

        private static float Wrap(float d, float length, bool closed)
        {
            return closed ? Mathf.Repeat(d, length) : Mathf.Clamp(d, 0f, length);
        }

        private void BuildGround(Network net)
        {
            if (_mesh == null) { _mesh = new Mesh { name = "Pathway", hideFlags = HideFlags.DontSave }; }
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _mesh.Clear();

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            // Only write when it changed, so reopening a scene doesn't mark it dirty.
            if (_groundMaterial != null && meshRenderer.sharedMaterial != _groundMaterial) { meshRenderer.sharedMaterial = _groundMaterial; }
            if (!_showGround) { return; }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float half = _width * 0.5f;
            // About one column a metre across, so bumpy ground snapped under the middle doesn't poke through.
            int columns = Mathf.Max(2, Mathf.CeilToInt(_width) + 1);
            for (int s = 0; s < net.Splines.Count; s++)
            {
                Spline spline = net.Splines[s];
                if (spline == null) { continue; }
                // Each spline a hair higher than the last, so paths crossing without a junction don't flicker.
                Vector3 lift = Vector3.up * (_groundHeight + s * 0.002f);
                foreach (var (from, to) in StripRuns(net, s))
                {
                    int segments = Mathf.Max(1, Mathf.CeilToInt((to - from) / _step));
                    int start = verts.Count;
                    for (int i = 0; i <= segments; i++)
                    {
                        float d = Mathf.Lerp(from, to, (float)i / segments);
                        for (int c = 0; c < columns; c++)
                        {
                            float u = (float)c / (columns - 1);
                            verts.Add(SidePoint(spline, d, Mathf.Lerp(-half, half, u), true, out _) + lift);
                            uvs.Add(new Vector2(u, d / _textureLength));
                        }
                    }
                    for (int i = 0; i < segments; i++)
                    {
                        int row = start + i * columns, next = row + columns;
                        for (int c = 0; c < columns - 1; c++)
                        {
                            int l0 = row + c, r0 = l0 + 1, l1 = next + c, r1 = l1 + 1;
                            // Cross(p1 - p0, p2 - p0) points up, so the strip faces the sky.
                            tris.Add(l0); tris.Add(l1); tris.Add(r0);
                            tris.Add(r0); tris.Add(l1); tris.Add(r1);
                        }
                    }
                }
            }

            foreach (Junction j in net.Junctions) { AddPatch(net, j, half, verts, uvs, tris); }

            _mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateTangents();
            _mesh.RecalculateBounds();
        }

        // Fills a junction: a fan from the middle through both edge corners of every arm's trimmed strip end.
        private void AddPatch(Network net, Junction j, float half, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            // Each rim corner gets its own strip's lift (see BuildGround), so the patch meets every strip without a step.
            var rim = new List<Vector3>();
            float highest = 0f;
            foreach (Arm arm in j.Arms)
            {
                Spline spline = net.Splines[arm.Spline];
                float d = Wrap(arm.Trim, net.Lengths[arm.Spline], spline.Closed);
                float lift = _groundHeight + arm.Spline * 0.002f;
                highest = Mathf.Max(highest, lift);
                // The same points across as the strip's end row, so no crack opens between them on bumpy ground.
                int columns = Mathf.Max(2, Mathf.CeilToInt(_width) + 1);
                for (int c = 0; c < columns; c++)
                {
                    rim.Add(SidePoint(spline, d, Mathf.Lerp(-half, half, (float)c / (columns - 1)), true, out _) + Vector3.up * lift);
                }
            }
            Vector3 centre = (_snapToGround ? Snap(j.Centre) : j.Centre) + Vector3.up * highest;
            rim.Sort((a, b) => Mathf.Atan2(a.z - centre.z, a.x - centre.x).CompareTo(Mathf.Atan2(b.z - centre.z, b.x - centre.x)));

            int start = verts.Count;
            verts.Add(centre);
            verts.AddRange(rim);
            for (int i = start; i < verts.Count; i++) { uvs.Add(new Vector2(verts[i].x, verts[i].z) / _textureLength); }
            for (int i = 0; i < rim.Count; i++)
            {
                int a = start + 1 + i, b = start + 1 + (i + 1) % rim.Count;
                // The rim runs counter-clockwise seen from above, so (centre, next, this) faces the sky.
                tris.Add(start); tris.Add(b); tris.Add(a);
            }
        }
    }
}
