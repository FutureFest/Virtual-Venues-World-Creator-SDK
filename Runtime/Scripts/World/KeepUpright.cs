using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Holds this object's starting world rotation while its parent spins: ferris-wheel pods, hanging lanterns.
    /// Local only, nothing networked — it just follows whatever its parent does.
    /// </summary>
    public class KeepUpright : MonoBehaviour
    {
        private Quaternion _rotation = Quaternion.identity;

        // Start, not Awake: a world loader may place the object after it is created.
        private void Start()
        {
            _rotation = transform.rotation;
        }

        private void LateUpdate()
        {
            transform.rotation = _rotation;
        }
    }
}
