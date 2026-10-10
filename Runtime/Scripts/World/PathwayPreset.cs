using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// A saved path kit (ground + prop rules), e.g. "Park Path": lamp posts, a fence and flowers. Load it onto any
    /// <see cref="Pathway"/> from its Inspector, or save one from a path you like.
    /// </summary>
    [CreateAssetMenu(menuName = "VirtualVenues/Pathway Kit", fileName = "PathwayKit")]
    public class PathwayPreset : ScriptableObject
    {
        [SerializeField] private bool _showGround = true;
        [Min(0.1f)] [SerializeField] private float _width = 3f;
        [Min(0.1f)] [SerializeField] private float _textureLength = 3f;
        [SerializeField] private float _groundHeight = 0.05f;
        [SerializeField] private Material _groundMaterial = null;
        [SerializeField] private List<PathwayRule> _rules = new List<PathwayRule>();

        public bool ShowGround => _showGround;
        public float Width => _width;
        public float TextureLength => _textureLength;
        public float GroundHeight => _groundHeight;
        public Material GroundMaterial => _groundMaterial;
        public List<PathwayRule> Rules => _rules;

        public void Set(bool showGround, float width, float textureLength, float groundHeight, Material groundMaterial, IEnumerable<PathwayRule> rules)
        {
            _showGround = showGround;
            _width = Mathf.Max(0.1f, width);
            _textureLength = Mathf.Max(0.1f, textureLength);
            _groundHeight = groundHeight;
            _groundMaterial = groundMaterial;
            _rules = new List<PathwayRule>();
            foreach (PathwayRule rule in rules) { if (rule != null) { _rules.Add(rule.Clone()); } }
        }
    }
}
