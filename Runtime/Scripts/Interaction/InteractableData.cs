using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public abstract class InteractableData : MonoBehaviour
    {
        public enum InteractbaleType
        {
            None,
            BrowserBridge,
            Seat, // append only — serialized as int
        }

        public abstract InteractbaleType InteractableType { get; }
    }
}
