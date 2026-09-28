using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// A hotbar action players own (dance, VFX, spray). Publish the asset with the Avatar Publisher; it picks
    /// its own slot. Data only - the core project plays it.
    /// </summary>
    public abstract class ActionContent : ScriptableObject
    {
        [Tooltip("Hotbar icon for this action.")]
        [SerializeField] private Sprite _icon = null;

        public Sprite Icon => _icon;

        /// <summary>The slot / Addressables prefix the runtime loads this from ("Dance", "VFX" or "Spray").</summary>
        public abstract string Category { get; }
    }
}
