using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>A graffiti tag the player sprays onto surfaces.</summary>
    [CreateAssetMenu(fileName = "SprayAction", menuName = "Virtual Venues/Actions/Spray")]
    public class SprayAction : ActionContent
    {
        [Tooltip("Decal material. Must use the URP \"Shader Graphs/Decal\" shader.")]
        [SerializeField] private Material _decal = null;

        public Material Decal => _decal;
        public override string Category => "Spray";
    }
}
