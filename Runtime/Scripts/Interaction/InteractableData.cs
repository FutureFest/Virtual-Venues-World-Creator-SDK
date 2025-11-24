using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public abstract class InteractableData : MonoBehaviour
    {
        public enum InteractbaleType
        {
            None,
            BrowserBridge,
        }

        public abstract InteractbaleType InteractableType { get; }
    }
}
