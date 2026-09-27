using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Turns this object to face the main camera every frame. Signs, name tags, highlight messages.
    /// Local only — each client faces its own camera, so nothing is networked.
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        [Tooltip("Optional. Use this transform's up instead of world up (e.g. a tilted board).")]
        [SerializeField] private Transform _upOverride = null;
        [Tooltip("Added to the camera position before looking at it.")]
        [SerializeField] private Vector3 _lookOffset = Vector3.zero;
        [Tooltip("Face away from the camera. For meshes whose front is -Z.")]
        [SerializeField] private bool _flipZ = false;

        public void SetWorldUpOverride(Transform upOverride)
        {
            _upOverride = upOverride;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) { return; }

            Vector3 lookPoint = cam.transform.position + _lookOffset;
            if (_flipZ) { lookPoint = transform.position + (transform.position - lookPoint); }

            transform.LookAt(lookPoint, _upOverride != null ? _upOverride.up : Vector3.up);
        }
    }
}
