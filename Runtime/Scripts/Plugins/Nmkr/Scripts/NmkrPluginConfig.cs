using UnityEngine;

namespace VirtualVenues.Plugins.Nmkr
{
    /// <summary>
    /// One NMKR Studio account's credentials. Preprod/testnet and mainnet are
    /// entirely separate NMKR accounts with separate API keys, so a config
    /// holds one <see cref="NmkrAccount"/> per environment.
    /// </summary>
    [System.Serializable]
    public class NmkrAccount
    {
        [SerializeField] private int _customerId = 0;
        [SerializeField] private string _apiKey = string.Empty;
        [SerializeField] private string _payoutWalletAddress = string.Empty;
        [SerializeField] private string _displayName = string.Empty;

        public int CustomerId => _customerId;
        public string ApiKey => _apiKey;
        public string PayoutWalletAddress => _payoutWalletAddress;
        public string DisplayName => _displayName;
    }

    [CreateAssetMenu(menuName = "Virtual Venues/Plugins/NMKR Config", fileName = "NmkrPluginConfig")]
    public class NmkrPluginConfig : ScriptableObject
    {
        [SerializeField] private NmkrEnvironment _environment = NmkrEnvironment.Preprod;
        [SerializeField] private NmkrAccount _preprod = new NmkrAccount();
        [SerializeField] private NmkrAccount _mainnet = new NmkrAccount();

        /// <summary>The network whose account is currently active.</summary>
        public NmkrEnvironment Environment => _environment;

        public NmkrAccount Preprod => _preprod;
        public NmkrAccount Mainnet => _mainnet;

        /// <summary>The account for the currently-selected <see cref="Environment"/>.</summary>
        public NmkrAccount ActiveAccount => _environment == NmkrEnvironment.Mainnet ? _mainnet : _preprod;

        // Convenience pass-throughs to the active account, so consumers that
        // only care about "the credentials in use" don't have to branch.
        public int CustomerId => ActiveAccount.CustomerId;
        public string ApiKey => ActiveAccount.ApiKey;
        public string PayoutWalletAddress => ActiveAccount.PayoutWalletAddress;
        public string DisplayName => ActiveAccount.DisplayName;
    }

    public enum NmkrEnvironment
    {
        Preprod = 0,
        Mainnet = 1
    }
}
