using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>A dance emote: the override controller swaps the player's dance clip while it plays.</summary>
    [CreateAssetMenu(fileName = "DanceAction", menuName = "Virtual Venues/Actions/Dance")]
    public class DanceAction : ActionContent
    {
        [Tooltip("Overrides the player's dance clip with yours.")]
        [SerializeField] private AnimatorOverrideController _overrideController = null;

        public AnimatorOverrideController OverrideController => _overrideController;
        public override string Category => "Dance";
    }
}
