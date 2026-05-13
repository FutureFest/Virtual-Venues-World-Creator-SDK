using UnityEngine;

namespace VirtualVenues.Plugins.Nmkr
{
    [CreateAssetMenu(menuName = "Virtual Venues/Plugins/NMKR Config", fileName = "NmkrPluginConfig")]
    public class NmkrPluginConfig : ScriptableObject
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

        public void SetAll(int customerId, string apiKey, NmkrEnvironment environment, string payoutWalletAddress, string displayName)
        {
            _customerId = customerId;
            _apiKey = apiKey;
            _environment = environment;
            _payoutWalletAddress = payoutWalletAddress;
            _displayName = displayName;
        }
    }

    public enum NmkrEnvironment
    {
        Preprod = 0,
        Mainnet = 1
    }
}
