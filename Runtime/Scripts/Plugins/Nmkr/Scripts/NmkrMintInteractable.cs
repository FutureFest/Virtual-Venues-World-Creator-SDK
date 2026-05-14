using UnityEngine;
using VirtualVenues.WorldCreator;

namespace VirtualVenues.Plugins.Nmkr
{
    /// <summary>
    /// A creator-placed interactable that targets one NMKR project for minting.
    /// Pure data carrier: it holds the project UID plus optional display info,
    /// and relies on the base <see cref="Interactable"/> for proximity,
    /// highlighting, and the interact event.
    /// </summary>
    /// <remarks>
    /// Do NOT add an Awake() to this class. <see cref="Interactable"/>.Awake()
    /// is private and registers the instance into Interactable.Tracker. Unity
    /// only invokes the most-derived Awake, so declaring one here would silently
    /// hide the base and break tracker registration. This component needs no
    /// Awake logic: the FFXR-side runtime bridge discovers instances via
    /// Interactable.Tracker and subscribes to the inherited OnLocalInteractEvent.
    /// </remarks>
    [AddComponentMenu("Virtual Venues/Plugins/NMKR/Mint Interactable")]
    public class NmkrMintInteractable : Interactable
    {
        [Header("NMKR Drop")]
        [NmkrProjectUid]
        [SerializeField] private string _projectUid = string.Empty;
        [SerializeField, Min(1)] private int _nftCount = 1;

        [Header("Display (optional)")]
        [SerializeField] private string _dropTitle = string.Empty;
        [SerializeField, TextArea] private string _dropDescription = string.Empty;
        [SerializeField] private Sprite _dropImage = null;

        public string ProjectUid => _projectUid;
        public int NftCount => _nftCount;
        public string DropTitle => _dropTitle;
        public string DropDescription => _dropDescription;
        public Sprite DropImage => _dropImage;
    }
}
