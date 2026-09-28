using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>A particle burst the player emits from their hands.</summary>
    [CreateAssetMenu(fileName = "VfxAction", menuName = "Virtual Venues/Actions/VFX")]
    public class VfxAction : ActionContent
    {
        [Tooltip("Prefab with a ParticleSystem on its root.")]
        [SerializeField] private GameObject _vfxPrefab = null;
        [Tooltip("Particles emitted per use.")]
        [SerializeField] private int _emitAmount = 3;

        public GameObject VFXPrefab => _vfxPrefab;
        public int EmitAmount => _emitAmount;
        public override string Category => "VFX";
    }
}
