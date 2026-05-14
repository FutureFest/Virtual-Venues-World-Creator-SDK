using UnityEngine;

namespace VirtualVenues.Plugins.Nmkr
{
    /// <summary>
    /// Carries a Host's NMKR account settings on the creator-spawned
    /// NMKR_Plugin prefab so they travel inside the published world bundle.
    /// FFXR's runtime bridge reads these fields from the loaded world scene.
    /// </summary>
    /// <remarks>
    /// On disk these fields are intentionally left empty. The creator edits the
    /// shared <see cref="NmkrPluginConfig"/> asset (via the NMKR Settings window
    /// or this component's custom Inspector), and NmkrBuildProcessor injects the
    /// values at build time: the full set into the UMS / dedicated-server
    /// bundle, everything except the apiKey into the UPC client bundle — so the
    /// key never reaches an untrusted client.
    /// </remarks>
    [AddComponentMenu("Virtual Venues/Plugins/NMKR/NMKR Plugin Settings")]
    [DisallowMultipleComponent]
    public class NmkrPluginSettings : MonoBehaviour
    {
        [SerializeField] private int _customerId = 0;
        [SerializeField] private string _apiKey = string.Empty;
        [SerializeField] private NmkrEnvironment _environment = NmkrEnvironment.Preprod;
        [SerializeField] private string _payoutWalletAddress = string.Empty;
        [SerializeField] private string _displayName = string.Empty;

        public int CustomerId => _customerId;
        public string ApiKey => _apiKey;
        public NmkrEnvironment Environment => _environment;
        public string PayoutWalletAddress => _payoutWalletAddress;
        public string DisplayName => _displayName;
    }
}
