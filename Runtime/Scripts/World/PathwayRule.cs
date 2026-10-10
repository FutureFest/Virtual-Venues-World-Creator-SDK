using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>Which side of the path a <see cref="PathwayRule"/> places its props on.</summary>
    public enum PathwayLane
    {
        Center = 0,
        Left = 1,
        Right = 2,
        BothSides = 3,
        Alternating = 4,
    }

    /// <summary>Where along the path a <see cref="PathwayRule"/> places its props.</summary>
    public enum PathwaySlot
    {
        /// <summary>One prop every Spacing metres (lamps, plants, benches).</summary>
        Every = 0,
        /// <summary>Pieces that span Spacing metres each, stretched to meet on curves (fences, walls, hedges).</summary>
        Segments = 1,
        /// <summary>One prop at the start of the path (gate, sign).</summary>
        Start = 2,
        /// <summary>One prop at the end of the path.</summary>
        End = 3,
        /// <summary>One prop at each path point where the path turns by Corner Angle or more.</summary>
        Corners = 4,
        /// <summary>Where paths meet: one prop on each corner between two joining paths, or one in the middle on the Center lane.</summary>
        Junctions = 5,
    }

    /// <summary>Which way a <see cref="PathwayRule"/>'s props face.</summary>
    public enum PathwayFacing
    {
        AlongPath = 0,
        TowardPath = 1,
        AwayFromPath = 2,
        Random = 3,
    }

    /// <summary>One prefab a rule can pick, and how often relative to the rule's other prefabs.</summary>
    [Serializable]
    public class PathwayProp
    {
        [Tooltip("The prop to place. Use script-free prefabs, or only SDK / Unity scripts: other scripts don't survive into the app.")]
        [SerializeField] private GameObject _prefab = null;
        [Tooltip("How often this prop is picked compared to the rule's other props. 2 = twice as often as 1.")]
        [Min(0f)] [SerializeField] private float _weight = 1f;

        public GameObject Prefab => _prefab;
        public float Weight => _weight;

        public PathwayProp() { }

        public PathwayProp(GameObject prefab, float weight = 1f)
        {
            _prefab = prefab;
            _weight = Mathf.Max(0f, weight);
        }
    }

    /// <summary>
    /// One layer of props along a <see cref="Pathway"/>: which prefabs, where along the path, which side, and how they
    /// face. Each rule draws its own random numbers, so editing one rule never reshuffles another.
    /// </summary>
    [Serializable]
    public class PathwayRule
    {
        [Tooltip("Just a label, e.g. Lamps, Fence, Flowers.")]
        [SerializeField] private string _name = "Props";
        [Tooltip("Untick to hide this rule's props without losing its settings.")]
        [SerializeField] private bool _enabled = true;
        [Tooltip("The props to pick from. Each placement picks one at random, by weight.")]
        [SerializeField] private List<PathwayProp> _props = new List<PathwayProp>();
        [Tooltip("Every: one prop every Spacing metres.\nSegments: pieces Spacing metres long, end to end (fences, walls). " +
                 "Make Spacing the length of one piece.\nStart / End: one prop at that end of the path.\n" +
                 "Corners: one prop at each path point where the path turns by Corner Angle or more.\n" +
                 "Junctions: where paths meet, one prop on each corner between two joining paths (Offset = metres past the edges), " +
                 "or one in the middle on the Center lane (fountain, signpost).")]
        [SerializeField] private PathwaySlot _slot = PathwaySlot.Every;
        [Tooltip("Which side of the path. Alternating swaps sides at each placement.")]
        [SerializeField] private PathwayLane _lane = PathwayLane.BothSides;
        [Tooltip("Metres beyond the edge of the path (negative = in from the edge). On the Center lane: metres to the right of the middle.")]
        [SerializeField] private float _offset = 0.5f;
        [Tooltip("Metres between props (Every), or the length of one piece (Segments).")]
        [Min(0.1f)] [SerializeField] private float _spacing = 6f;
        [Tooltip("Metres to leave empty at the start of the path.")]
        [Min(0f)] [SerializeField] private float _startInset = 0f;
        [Tooltip("Metres to leave empty at the end of the path.")]
        [Min(0f)] [SerializeField] private float _endInset = 0f;
        [Tooltip("Every only: nudge each prop forward or back by up to this fraction of Spacing, so the row looks less regular.")]
        [Range(0f, 1f)] [SerializeField] private float _jitter = 0f;
        [Tooltip("Chance each placement gets a prop. 1 = always, 0.5 = about half of them.")]
        [Range(0f, 1f)] [SerializeField] private float _chance = 1f;
        [Tooltip("Which way the props face. Toward / Away From Path turn them to face the middle of the path or away from it.")]
        [SerializeField] private PathwayFacing _facing = PathwayFacing.AlongPath;
        [Tooltip("Turn each prop by a random amount up to this many degrees either way.")]
        [Range(0f, 180f)] [SerializeField] private float _randomTurn = 0f;
        [Tooltip("Smallest random size (1 = the prefab's own size).")]
        [Min(0.01f)] [SerializeField] private float _minScale = 1f;
        [Tooltip("Largest random size (1 = the prefab's own size).")]
        [Min(0.01f)] [SerializeField] private float _maxScale = 1f;
        [Tooltip("Keep-clear radius in metres. A prop is skipped if it would land this close to an earlier prop that also has " +
                 "a keep-clear radius (earlier rules win). 0 = never skipped.")]
        [Min(0f)] [SerializeField] private float _clearance = 0f;
        [Tooltip("Corners only: how sharply the path must turn at a point (degrees) to get a prop.")]
        [Range(1f, 180f)] [SerializeField] private float _cornerAngle = 30f;
        [Tooltip("Drop the props onto the ground below the path when the path's Snap To Ground is on.")]
        [SerializeField] private bool _followGround = true;

        public string Name { get => _name; set => _name = value; }
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public List<PathwayProp> Props => _props;
        public PathwaySlot Slot { get => _slot; set => _slot = value; }
        public PathwayLane Lane { get => _lane; set => _lane = value; }
        public float Offset { get => _offset; set => _offset = value; }
        public float Spacing { get => _spacing; set => _spacing = Mathf.Max(0.1f, value); }
        public float StartInset { get => _startInset; set => _startInset = Mathf.Max(0f, value); }
        public float EndInset { get => _endInset; set => _endInset = Mathf.Max(0f, value); }
        public float Jitter { get => _jitter; set => _jitter = Mathf.Clamp01(value); }
        public float Chance { get => _chance; set => _chance = Mathf.Clamp01(value); }
        public PathwayFacing Facing { get => _facing; set => _facing = value; }
        public float RandomTurn { get => _randomTurn; set => _randomTurn = Mathf.Clamp(value, 0f, 180f); }
        public float MinScale { get => _minScale; set => _minScale = Mathf.Max(0.01f, value); }
        public float MaxScale { get => _maxScale; set => _maxScale = Mathf.Max(0.01f, value); }
        public float Clearance { get => _clearance; set => _clearance = Mathf.Max(0f, value); }
        public float CornerAngle { get => _cornerAngle; set => _cornerAngle = Mathf.Clamp(value, 1f, 180f); }
        public bool FollowGround { get => _followGround; set => _followGround = value; }

        public PathwayRule() { }

        public PathwayRule(string name, PathwaySlot slot, PathwayLane lane, float spacing, params GameObject[] prefabs)
        {
            _name = name;
            _slot = slot;
            _lane = lane;
            Spacing = spacing;
            foreach (GameObject prefab in prefabs) { _props.Add(new PathwayProp(prefab)); }
        }

        /// <summary>A copy that shares the prefabs but not the list, so editing one never changes the other.</summary>
        public PathwayRule Clone()
        {
            var copy = (PathwayRule)MemberwiseClone();
            copy._props = new List<PathwayProp>(_props);
            return copy;
        }

        /// <summary>Weighted pick from the props; <paramref name="roll"/> is 0..1. Null when there is nothing to pick.</summary>
        public GameObject Pick(double roll)
        {
            float total = 0f;
            foreach (PathwayProp p in _props) { if (p != null && p.Prefab != null) { total += p.Weight; } }
            if (total <= 0f) { return null; }
            float target = (float)roll * total;
            GameObject last = null;
            foreach (PathwayProp p in _props)
            {
                if (p == null || p.Prefab == null || p.Weight <= 0f) { continue; }
                last = p.Prefab;
                target -= p.Weight;
                if (target < 0f) { return p.Prefab; }
            }
            return last;
        }
    }
}
