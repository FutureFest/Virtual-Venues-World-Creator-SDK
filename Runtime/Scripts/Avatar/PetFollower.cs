using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Marks a prefab as a pet: a cosmetic companion that follows its owner and mirrors the owner's
    /// movement state. Publish it in the Avatar Publisher under the "Pet" slot.
    /// Data only — the core project adds the follow/animation behaviour when the pet is summoned.
    /// </summary>
    public class PetFollower : MonoBehaviour
    {
        // Pet_Anim_Control_Template.controller (Runtime/Animation/Pets). Looked up by GUID so it resolves
        // when the SDK is installed as a package.
        private const string TEMPLATE_CONTROLLER_GUID = "c9f6c7b4fc8fece41b8f1e920fd5e509";

        [Tooltip("The pet's Animator. Its controller must expose these Bool parameters (one is set true per state): " +
                 "IdleWalkRun, Jump, FreeFall (in air), Land, Dance, Sit, Drive; plus Float parameters Speed and MotionSpeed. " +
                 "A controller with plain states of those names (FreeFall as InAir) also works. " +
                 "Pet_Anim_Control_Template already does this and is assigned on Reset.")]
        [SerializeField] private Animator _animator = null;

        [Tooltip("Optional: swaps the template's clips for this pet's own (base it on Pet_Anim_Control_Template).")]
        [SerializeField] private AnimatorOverrideController _animationOverrides = null;

        [Tooltip("The pet's left 'eye': rays cast forward from here make it switch sides when a wall is ahead. Place halfway up the body.")]
        [SerializeField] private Transform _scanLeft = null;

        [Tooltip("The pet's right 'eye' (see Scan Left).")]
        [SerializeField] private Transform _scanRight = null;

        public Animator Animator => _animator;
        public AnimatorOverrideController AnimationOverrides => _animationOverrides;
        public Transform ScanLeft => _scanLeft;
        public Transform ScanRight => _scanRight;

        private void Reset()
        {
            if (_animator == null) { _animator = GetComponentInChildren<Animator>(); }
            if (_scanLeft == null || _scanRight == null) { CreateScanPoints(); }
#if UNITY_EDITOR
            if (_animator != null && _animator.runtimeAnimatorController == null)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(TEMPLATE_CONTROLLER_GUID);
                _animator.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
            }
#endif
        }

        private void CreateScanPoints()
        {
            Transform parent = new GameObject("Pet Collision Scans").transform;
            parent.SetParent(transform, false);
            parent.localPosition = new Vector3(0f, 0.5f, 0f);

            _scanLeft = new GameObject("Left Side").transform;
            _scanLeft.SetParent(parent, false);
            _scanLeft.localPosition = new Vector3(-0.5f, 0f, 0f);

            _scanRight = new GameObject("Right Side").transform;
            _scanRight.SetParent(parent, false);
            _scanRight.localPosition = new Vector3(0.5f, 0f, 0f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (_scanLeft != null) { Gizmos.DrawRay(_scanLeft.position, _scanLeft.forward * 2f); }
            if (_scanRight != null) { Gizmos.DrawRay(_scanRight.position, _scanRight.forward * 2f); }
        }
    }
}
